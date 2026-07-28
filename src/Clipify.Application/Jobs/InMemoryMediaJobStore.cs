using Clipify.Domain.Jobs;

namespace Clipify.Application.Jobs;

/// <summary>
/// Process-local store for Application unit tests. Not a substitute for SQLite persistence tests.
/// </summary>
public sealed class InMemoryMediaJobStore : IMediaJobStore
{
    private readonly object _gate = new();
    private readonly Dictionary<MediaJobId, MediaJobSnapshot> _jobs = new();
    private readonly Dictionary<MediaJobId, List<MediaArtifact>> _artifacts = new();

    public ValueTask InsertAsync(MediaJobSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_jobs.TryAdd(snapshot.Id, snapshot))
            {
                throw new InvalidOperationException($"Job '{snapshot.Id}' already exists.");
            }

            _artifacts[snapshot.Id] = [];
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<MediaJobSnapshot?> GetAsync(MediaJobId jobId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_jobs.TryGetValue(jobId, out var snapshot) ? Clone(snapshot) : null);
        }
    }

    public ValueTask<IReadOnlyList<MediaJobSnapshot>> ListAsync(
        MediaJobListQuery query,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IEnumerable<MediaJobSnapshot> items = _jobs.Values
                .OrderByDescending(j => j.CreatedAt)
                .ThenByDescending(j => j.Id.Value);

            if (query.States is { Count: > 0 })
            {
                items = items.Where(j => query.States.Contains(j.State));
            }

            var list = items.Skip(query.Skip).Take(Math.Clamp(query.Take, 1, 100))
                .Select(Clone)
                .ToList();
            return ValueTask.FromResult<IReadOnlyList<MediaJobSnapshot>>(list);
        }
    }

    public ValueTask UpdateProgressAsync(
        MediaJobId jobId,
        MediaJobProgress progress,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(jobId, out var snapshot))
            {
                return ValueTask.CompletedTask;
            }

            _jobs[jobId] = snapshot with
            {
                Progress = progress,
                Stage = progress.Stage,
                UpdatedAt = progress.UpdatedAt,
            };
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TransitionAsync(
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
        lock (_gate)
        {
            if (!_jobs.TryGetValue(jobId, out var snapshot) || snapshot.State != from)
            {
                return ValueTask.FromResult(false);
            }

            MediaJobStateTransitions.EnsureCanTransition(from, to);

            var updated = snapshot with
            {
                State = to,
                UpdatedAt = updatedAt,
                StartedAt = startedAt ?? snapshot.StartedAt,
                CompletedAt = completedAt ?? snapshot.CompletedAt,
                ErrorCode = errorCode ?? snapshot.ErrorCode,
                ErrorMessage = errorMessage ?? snapshot.ErrorMessage,
                Stage = stage ?? snapshot.Stage,
            };

            if (clearLeaseOwner is not null
                && string.Equals(updated.LeaseOwner, clearLeaseOwner, StringComparison.Ordinal))
            {
                updated = updated with
                {
                    LeaseOwner = null,
                    LeaseAcquiredAt = null,
                    LeaseExpiresAt = null,
                    HeartbeatAt = null,
                };
            }

            _jobs[jobId] = updated;
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<MediaJobCancelOutcome> CancelAsync(
        MediaJobId jobId,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(jobId, out var snapshot))
            {
                return ValueTask.FromResult(new MediaJobCancelOutcome(false, false, null));
            }

            if (MediaJobStateTransitions.IsTerminal(snapshot.State))
            {
                return ValueTask.FromResult(new MediaJobCancelOutcome(true, false, Clone(snapshot)));
            }

            MediaJobSnapshot updated;
            var stateChanged = false;

            switch (snapshot.State)
            {
                case MediaJobState.Queued:
                    MediaJobStateTransitions.EnsureCanTransition(MediaJobState.Queued, MediaJobState.Canceled);
                    updated = snapshot with
                    {
                        State = MediaJobState.Canceled,
                        UpdatedAt = requestedAt,
                        CompletedAt = requestedAt,
                        CancelRequestedAt = requestedAt,
                    };
                    stateChanged = true;
                    break;

                case MediaJobState.Running:
                    MediaJobStateTransitions.EnsureCanTransition(MediaJobState.Running, MediaJobState.Canceling);
                    updated = snapshot with
                    {
                        State = MediaJobState.Canceling,
                        UpdatedAt = requestedAt,
                        CancelRequestedAt = requestedAt,
                    };
                    stateChanged = true;
                    break;

                case MediaJobState.Canceling:
                    updated = snapshot.CancelRequestedAt is null
                        ? snapshot with { CancelRequestedAt = requestedAt, UpdatedAt = requestedAt }
                        : snapshot;
                    stateChanged = false;
                    break;

                default:
                    return ValueTask.FromResult(new MediaJobCancelOutcome(true, false, Clone(snapshot)));
            }

            _jobs[jobId] = updated;
            return ValueTask.FromResult(new MediaJobCancelOutcome(true, stateChanged, Clone(updated)));
        }
    }

    public ValueTask<bool> IsCancelRequestedAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(
                _jobs.TryGetValue(jobId, out var snapshot) && snapshot.CancelRequestedAt is not null);
        }
    }

    public ValueTask<MediaJobSnapshot?> TryClaimNextAsync(
        MediaJobClaimOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            // Count every Running/Canceling job, including those with expired leases that still
            // hold a job lock and therefore have not been repaired to Interrupted.
            var runningCount = _jobs.Values.Count(j =>
                j.State is MediaJobState.Running or MediaJobState.Canceling);

            if (runningCount >= options.MaxConcurrency)
            {
                return ValueTask.FromResult<MediaJobSnapshot?>(null);
            }

            var next = _jobs.Values
                .Where(j => j.State == MediaJobState.Queued)
                .OrderByDescending(j => j.Priority)
                .ThenBy(j => j.CreatedAt)
                .ThenBy(j => j.Id.Value)
                .FirstOrDefault();

            if (next is null)
            {
                return ValueTask.FromResult<MediaJobSnapshot?>(null);
            }

            var claimed = next with
            {
                State = MediaJobState.Running,
                UpdatedAt = now,
                StartedAt = now,
                LeaseOwner = options.LeaseOwner,
                LeaseAcquiredAt = now,
                LeaseExpiresAt = now + options.LeaseDuration,
                HeartbeatAt = now,
            };
            _jobs[next.Id] = claimed;
            return ValueTask.FromResult<MediaJobSnapshot?>(Clone(claimed));
        }
    }

    public ValueTask<bool> HeartbeatAsync(
        MediaJobId jobId,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(jobId, out var snapshot)
                || !string.Equals(snapshot.LeaseOwner, leaseOwner, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(false);
            }

            _jobs[jobId] = snapshot with
            {
                HeartbeatAt = now,
                LeaseExpiresAt = now + leaseDuration,
                UpdatedAt = now,
            };
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<int> RepairExpiredLeasesAsync(
        DateTimeOffset now,
        Func<MediaJobId, string?, bool> isJobLockHeldByOwner,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var repaired = 0;
            foreach (var snapshot in _jobs.Values.ToList())
            {
                if (snapshot.State is not (MediaJobState.Running or MediaJobState.Canceling))
                {
                    continue;
                }

                if (snapshot.LeaseExpiresAt is null || snapshot.LeaseExpiresAt > now)
                {
                    continue;
                }

                if (isJobLockHeldByOwner(snapshot.Id, snapshot.LeaseOwner))
                {
                    continue;
                }

                MediaJobStateTransitions.EnsureCanTransition(snapshot.State, MediaJobState.Interrupted);
                _jobs[snapshot.Id] = snapshot with
                {
                    State = MediaJobState.Interrupted,
                    UpdatedAt = now,
                    CompletedAt = now,
                    LeaseOwner = null,
                    LeaseAcquiredAt = null,
                    LeaseExpiresAt = null,
                    HeartbeatAt = null,
                    ErrorCode = "Interrupted",
                    ErrorMessage = "Lease expired and job lock was released.",
                };
                repaired++;
            }

            return ValueTask.FromResult(repaired);
        }
    }

    public ValueTask AddArtifactAsync(MediaArtifact artifact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        lock (_gate)
        {
            if (!_artifacts.TryGetValue(artifact.JobId, out var list))
            {
                list = [];
                _artifacts[artifact.JobId] = list;
            }

            // Idempotent by stable ArtifactId so post-commit retries do not duplicate rows.
            if (list.Any(a => string.Equals(a.ArtifactId, artifact.ArtifactId, StringComparison.Ordinal)))
            {
                return ValueTask.CompletedTask;
            }

            list.Add(artifact);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<MediaArtifact>> ListArtifactsAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_artifacts.TryGetValue(jobId, out var list))
            {
                return ValueTask.FromResult<IReadOnlyList<MediaArtifact>>([]);
            }

            return ValueTask.FromResult<IReadOnlyList<MediaArtifact>>(list.ToList());
        }
    }

    private static MediaJobSnapshot Clone(MediaJobSnapshot snapshot) =>
        snapshot with { Artifacts = snapshot.Artifacts.ToList() };
}

public sealed class InMemoryJobLock : IJobLock
{
    private readonly HashSet<MediaJobId> _held = [];
    private readonly object _gate = new();

    public ValueTask<IAsyncDisposable?> TryAcquireAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_held.Add(jobId))
            {
                return ValueTask.FromResult<IAsyncDisposable?>(null);
            }

            return ValueTask.FromResult<IAsyncDisposable?>(new Releaser(this, jobId));
        }
    }

    public bool IsHeld(MediaJobId jobId, string? leaseOwner)
    {
        lock (_gate)
        {
            return _held.Contains(jobId);
        }
    }

    private void Release(MediaJobId jobId)
    {
        lock (_gate)
        {
            _held.Remove(jobId);
        }
    }

    private sealed class Releaser(InMemoryJobLock owner, MediaJobId jobId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.Release(jobId);
            return ValueTask.CompletedTask;
        }
    }
}
