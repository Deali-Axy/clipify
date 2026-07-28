using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clipify.Application.Jobs;
using Clipify.Domain.Jobs;
using Clipify.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Clipify.Persistence.Jobs;

public sealed class EfMediaJobStore : IMediaJobStore
{
    private static readonly JsonSerializerOptions ProgressOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IDbContextFactory<ClipifyDbContext> _dbContextFactory;

    public EfMediaJobStore(IDbContextFactory<ClipifyDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async ValueTask InsertAsync(MediaJobSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await SqliteBusyRetry.ExecuteAsync(async ct =>
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            db.MediaJobs.Add(MediaJobMapper.ToEntity(snapshot));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<MediaJobSnapshot?> GetAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default)
    {
        return await SqliteBusyRetry.ExecuteAsync(async ct =>
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var entity = await db.MediaJobs.AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId.Value, ct)
                .ConfigureAwait(false);
            if (entity is null)
            {
                return null;
            }

            var artifacts = await db.MediaArtifacts.AsNoTracking()
                .Where(a => a.JobId == jobId.Value)
                .OrderBy(a => a.CreatedAtUnixMs)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return MediaJobMapper.ToSnapshot(entity, artifacts.Select(MediaJobMapper.ToArtifact).ToList());
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<MediaJobSnapshot>> ListAsync(
        MediaJobListQuery query,
        CancellationToken cancellationToken = default)
    {
        return await SqliteBusyRetry.ExecuteAsync(async ct =>
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var q = db.MediaJobs.AsNoTracking().AsQueryable();
            if (query.States is { Count: > 0 })
            {
                var states = query.States.Select(s => s.ToString()).ToArray();
                q = q.Where(j => states.Contains(j.State));
            }

            var take = Math.Clamp(query.Take, 1, 100);
            var entities = await q
                .OrderByDescending(j => j.CreatedAtUnixMs)
                .ThenByDescending(j => j.Id)
                .Skip(query.Skip)
                .Take(take)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return (IReadOnlyList<MediaJobSnapshot>)entities
                .Select(e => MediaJobMapper.ToSnapshot(e))
                .ToList();
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask UpdateProgressAsync(
        MediaJobId jobId,
        MediaJobProgress progress,
        CancellationToken cancellationToken = default)
    {
        var json = SerializeProgress(progress);
        var updatedAt = UnixTime.ToUnixMilliseconds(progress.UpdatedAt);

        await SqliteBusyRetry.ExecuteAsync(async ct =>
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            await db.MediaJobs
                .Where(j => j.Id == jobId.Value)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(j => j.ProgressJson, json)
                        .SetProperty(j => j.Stage, progress.Stage)
                        .SetProperty(j => j.UpdatedAtUnixMs, updatedAt),
                    ct)
                .ConfigureAwait(false);
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> TransitionAsync(
        MediaJobId jobId,
        MediaJobState from,
        MediaJobState to,
        DateTimeOffset updatedAt,
        string? errorCode = null,
        string? errorMessage = null,
        string? stage = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null,
        string? clearLeaseOwner = null,
        CancellationToken cancellationToken = default)
    {
        MediaJobStateTransitions.EnsureCanTransition(from, to);

        return await SqliteBusyRetry.ExecuteAsync(async ct =>
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            // Conditional UPDATE: Id + State=from. Affected-row count is the sole success signal,
            // so a concurrent CancelAsync that already moved State cannot be overwritten.
            command.CommandText =
                """
                UPDATE media_jobs
                SET State = $to,
                    UpdatedAtUnixMs = $updatedAt,
                    StartedAtUnixMs = COALESCE($startedAt, StartedAtUnixMs),
                    CompletedAtUnixMs = COALESCE($completedAt, CompletedAtUnixMs),
                    ErrorCode = COALESCE($errorCode, ErrorCode),
                    ErrorMessage = COALESCE($errorMessage, ErrorMessage),
                    Stage = COALESCE($stage, Stage),
                    LeaseAcquiredAtUnixMs = CASE
                        WHEN $clearOwner IS NOT NULL AND LeaseOwner = $clearOwner THEN NULL
                        ELSE LeaseAcquiredAtUnixMs END,
                    LeaseExpiresAtUnixMs = CASE
                        WHEN $clearOwner IS NOT NULL AND LeaseOwner = $clearOwner THEN NULL
                        ELSE LeaseExpiresAtUnixMs END,
                    HeartbeatAtUnixMs = CASE
                        WHEN $clearOwner IS NOT NULL AND LeaseOwner = $clearOwner THEN NULL
                        ELSE HeartbeatAtUnixMs END,
                    LeaseOwner = CASE
                        WHEN $clearOwner IS NOT NULL AND LeaseOwner = $clearOwner THEN NULL
                        ELSE LeaseOwner END
                WHERE Id = $id
                  AND State = $from;
                """;
            AddParam(command, "$to", to.ToString());
            AddParam(command, "$updatedAt", UnixTime.ToUnixMilliseconds(updatedAt));
            AddParam(command, "$startedAt", startedAt is { } s ? UnixTime.ToUnixMilliseconds(s) : DBNull.Value);
            AddParam(command, "$completedAt", completedAt is { } c ? UnixTime.ToUnixMilliseconds(c) : DBNull.Value);
            AddParam(command, "$errorCode", (object?)errorCode ?? DBNull.Value);
            AddParam(command, "$errorMessage", (object?)errorMessage ?? DBNull.Value);
            AddParam(command, "$stage", (object?)stage ?? DBNull.Value);
            AddParam(command, "$clearOwner", (object?)clearLeaseOwner ?? DBNull.Value);
            AddParam(command, "$id", jobId.Value);
            AddParam(command, "$from", from.ToString());

            var rows = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return rows == 1;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<MediaJobCancelOutcome> CancelAsync(
        MediaJobId jobId,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default)
    {
        return await SqliteBusyRetry.ExecuteAsync(async ct =>
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
            }

            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var dbTx = tx.GetDbTransaction();
            var requestedMs = UnixTime.ToUnixMilliseconds(requestedAt);
            var stateChanged = false;

            // Conditional updates avoid read-then-write races with TransitionAsync.
            var queuedRows = await ExecuteNonQueryAsync(
                    connection,
                    dbTx,
                    """
                    UPDATE media_jobs
                    SET State = 'Canceled',
                        CompletedAtUnixMs = $at,
                        CancelRequestedAtUnixMs = $at,
                        UpdatedAtUnixMs = $at
                    WHERE Id = $id
                      AND State = 'Queued';
                    """,
                    ct,
                    ("$at", requestedMs),
                    ("$id", jobId.Value))
                .ConfigureAwait(false);

            if (queuedRows == 1)
            {
                stateChanged = true;
            }
            else
            {
                var runningRows = await ExecuteNonQueryAsync(
                        connection,
                        dbTx,
                        """
                        UPDATE media_jobs
                        SET State = 'Canceling',
                            CancelRequestedAtUnixMs = $at,
                            UpdatedAtUnixMs = $at
                        WHERE Id = $id
                          AND State = 'Running';
                        """,
                        ct,
                        ("$at", requestedMs),
                        ("$id", jobId.Value))
                    .ConfigureAwait(false);

                if (runningRows == 1)
                {
                    stateChanged = true;
                }
                else
                {
                    _ = await ExecuteNonQueryAsync(
                            connection,
                            dbTx,
                            """
                            UPDATE media_jobs
                            SET CancelRequestedAtUnixMs = COALESCE(CancelRequestedAtUnixMs, $at),
                                UpdatedAtUnixMs = $at
                            WHERE Id = $id
                              AND State = 'Canceling';
                            """,
                            ct,
                            ("$at", requestedMs),
                            ("$id", jobId.Value))
                        .ConfigureAwait(false);
                }
            }

            var entity = await db.MediaJobs.AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId.Value, ct)
                .ConfigureAwait(false);

            if (entity is null)
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                return new MediaJobCancelOutcome(false, false, null);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return new MediaJobCancelOutcome(true, stateChanged, MediaJobMapper.ToSnapshot(entity));
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> IsCancelRequestedAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default)
    {
        return await SqliteBusyRetry.ExecuteAsync(async ct =>
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            return await db.MediaJobs.AsNoTracking()
                .Where(j => j.Id == jobId.Value)
                .Select(j => j.CancelRequestedAtUnixMs != null)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<MediaJobSnapshot?> TryClaimNextAsync(
        MediaJobClaimOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LeaseOwner);

        return await SqliteBusyRetry.ExecuteAsync(async ct =>
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var connection = db.Database.GetDbConnection();
            var dbTx = tx.GetDbTransaction();

            var nowMs = UnixTime.ToUnixMilliseconds(now);
            var expiresMs = UnixTime.ToUnixMilliseconds(now + options.LeaseDuration);

            var runningCount = await CountActiveJobsAsync(connection, dbTx, ct).ConfigureAwait(false);
            if (runningCount >= options.MaxConcurrency)
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                return null;
            }

            var jobId = await SelectNextQueuedIdAsync(connection, dbTx, ct).ConfigureAwait(false);
            if (jobId is null)
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                return null;
            }

            var rows = await ClaimJobAsync(
                    connection,
                    dbTx,
                    jobId,
                    options.LeaseOwner,
                    nowMs,
                    expiresMs,
                    ct)
                .ConfigureAwait(false);

            if (rows != 1)
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                return null;
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);

            var entity = await db.MediaJobs.AsNoTracking()
                .FirstAsync(j => j.Id == jobId, ct)
                .ConfigureAwait(false);
            return MediaJobMapper.ToSnapshot(entity);
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> HeartbeatAsync(
        MediaJobId jobId,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);

        return await SqliteBusyRetry.ExecuteAsync(async ct =>
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE media_jobs
                SET HeartbeatAtUnixMs = $now,
                    LeaseExpiresAtUnixMs = $expires,
                    UpdatedAtUnixMs = $now
                WHERE Id = $id
                  AND LeaseOwner = $owner
                  AND State IN ('Running', 'Canceling');
                """;
            AddParam(command, "$now", UnixTime.ToUnixMilliseconds(now));
            AddParam(command, "$expires", UnixTime.ToUnixMilliseconds(now + leaseDuration));
            AddParam(command, "$id", jobId.Value);
            AddParam(command, "$owner", leaseOwner);
            var rows = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return rows == 1;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> RepairExpiredLeasesAsync(
        DateTimeOffset now,
        Func<MediaJobId, string?, bool> isJobLockHeldByOwner,
        CancellationToken cancellationToken = default)
    {
        return await SqliteBusyRetry.ExecuteAsync(async ct =>
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var nowMs = UnixTime.ToUnixMilliseconds(now);
            var candidates = await db.MediaJobs
                .Where(j =>
                    (j.State == nameof(MediaJobState.Running) || j.State == nameof(MediaJobState.Canceling))
                    && j.LeaseExpiresAtUnixMs != null
                    && j.LeaseExpiresAtUnixMs < nowMs)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var repaired = 0;
            foreach (var entity in candidates)
            {
                var jobId = MediaJobId.Parse(entity.Id);
                if (isJobLockHeldByOwner(jobId, entity.LeaseOwner))
                {
                    continue;
                }

                var from = Enum.Parse<MediaJobState>(entity.State);
                MediaJobStateTransitions.EnsureCanTransition(from, MediaJobState.Interrupted);

                entity.State = nameof(MediaJobState.Interrupted);
                entity.UpdatedAtUnixMs = nowMs;
                entity.CompletedAtUnixMs = nowMs;
                entity.LeaseOwner = null;
                entity.LeaseAcquiredAtUnixMs = null;
                entity.LeaseExpiresAtUnixMs = null;
                entity.HeartbeatAtUnixMs = null;
                entity.ErrorCode = "Interrupted";
                entity.ErrorMessage = "Lease expired and job lock was released.";
                repaired++;
            }

            if (repaired > 0)
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return repaired;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask AddArtifactAsync(MediaArtifact artifact, CancellationToken cancellationToken = default)
    {
        await SqliteBusyRetry.ExecuteAsync(async ct =>
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            db.MediaArtifacts.Add(MediaJobMapper.ToEntity(artifact));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<MediaArtifact>> ListArtifactsAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default)
    {
        return await SqliteBusyRetry.ExecuteAsync(async ct =>
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var entities = await db.MediaArtifacts.AsNoTracking()
                .Where(a => a.JobId == jobId.Value)
                .OrderBy(a => a.CreatedAtUnixMs)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<MediaArtifact>)entities.Select(MediaJobMapper.ToArtifact).ToList();
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string SerializeProgress(MediaJobProgress progress)
    {
        var dto = new
        {
            fraction = progress.Fraction,
            processed_duration_ms = progress.ProcessedDuration?.TotalMilliseconds,
            total_duration_ms = progress.TotalDuration?.TotalMilliseconds,
            speed = progress.Speed,
            estimated_remaining_ms = progress.EstimatedRemaining?.TotalMilliseconds,
            stage = progress.Stage,
            message = progress.Message,
            updated_at_unix_ms = UnixTime.ToUnixMilliseconds(progress.UpdatedAt),
        };
        return JsonSerializer.Serialize(dto, ProgressOptions);
    }

    private static async Task<int> CountActiveJobsAsync(
        DbConnection connection,
        DbTransaction tx,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        // Include expired-lease Running/Canceling rows. Repair only frees the slot after the
        // job lock is released; until then the job is still occupying concurrency capacity.
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM media_jobs
            WHERE State IN ('Running', 'Canceling');
            """;
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    private static async Task<string?> SelectNextQueuedIdAsync(
        DbConnection connection,
        DbTransaction tx,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            SELECT Id
            FROM media_jobs
            WHERE State = 'Queued'
            ORDER BY Priority DESC, CreatedAtUnixMs ASC, Id ASC
            LIMIT 1;
            """;
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result as string ?? result?.ToString();
    }

    private static async Task<int> ClaimJobAsync(
        DbConnection connection,
        DbTransaction tx,
        string jobId,
        string leaseOwner,
        long nowMs,
        long expiresMs,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            """
            UPDATE media_jobs
            SET State = 'Running',
                LeaseOwner = $owner,
                LeaseAcquiredAtUnixMs = $now,
                LeaseExpiresAtUnixMs = $expires,
                HeartbeatAtUnixMs = $now,
                StartedAtUnixMs = COALESCE(StartedAtUnixMs, $now),
                UpdatedAtUnixMs = $now
            WHERE Id = $id
              AND State = 'Queued';
            """;
        AddParam(command, "$owner", leaseOwner);
        AddParam(command, "$now", nowMs);
        AddParam(command, "$expires", expiresMs);
        AddParam(command, "$id", jobId);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ExecuteNonQueryAsync(
        DbConnection connection,
        DbTransaction tx,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            AddParam(command, name, value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddParam(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
