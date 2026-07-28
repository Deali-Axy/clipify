using Clipify.Application.Jobs;
using Clipify.Domain.Jobs;
using Clipify.Persistence;
using Clipify.Persistence.Locks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clipify.Persistence.Tests;

public sealed class SqliteTestFixture : IAsyncLifetime
{
    public string Root { get; }
    public string DatabasePath { get; }
    public string LockDirectory { get; }
    public ServiceProvider Services { get; private set; } = null!;
    public IMediaJobStore Store => Services.GetRequiredService<IMediaJobStore>();
    public IJobLock JobLock => Services.GetRequiredService<IJobLock>();
    public IDbContextFactory<ClipifyDbContext> DbFactory =>
        Services.GetRequiredService<IDbContextFactory<ClipifyDbContext>>();

    public SqliteTestFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "clipify-tests", Guid.NewGuid().ToString("N"));
        DatabasePath = Path.Combine(Root, "clipify.db");
        LockDirectory = Path.Combine(Root, "locks");
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LockDirectory);

        var services = new ServiceCollection();
        services.AddClipifyPersistence(new ClipifyPersistenceOptions
        {
            DatabasePath = DatabasePath,
            LockDirectory = LockDirectory,
        });
        Services = services.BuildServiceProvider();
        await Services.MigrateClipifyDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await Services.DisposeAsync();
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup on Windows file locks.
        }
    }
}

public class EfMediaJobStoreTests : IAsyncLifetime
{
    private readonly SqliteTestFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task Migration_creates_schema_on_real_sqlite_file()
    {
        Assert.True(File.Exists(_fx.DatabasePath));
        await using var db = await _fx.DbFactory.CreateDbContextAsync();
        Assert.True(await db.Database.CanConnectAsync());
        var pending = await db.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);
    }

    [Fact]
    public async Task Insert_get_and_artifact_roundtrip()
    {
        var id = MediaJobId.New();
        var now = DateTimeOffset.UtcNow;
        var snapshot = MediaJobSnapshot.CreateQueued(id, new FakeDelayJobDefinition { Label = "map" }, now);
        await _fx.Store.InsertAsync(snapshot);

        var loaded = await _fx.Store.GetAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal(MediaJobState.Queued, loaded.State);
        Assert.Equal(FakeDelayJobDefinition.Discriminator, loaded.DefinitionKind);

        var artifact = MediaArtifact.Create(id, "fake_output", "fake://out", now, sizeBytes: 12);
        await _fx.Store.AddArtifactAsync(artifact);
        var artifacts = await _fx.Store.ListArtifactsAsync(id);
        Assert.Single(artifacts);
        Assert.Equal(12, artifacts[0].SizeBytes);
    }

    [Fact]
    public async Task Claim_is_fifo_and_respects_concurrency()
    {
        var t0 = DateTimeOffset.UtcNow;
        var first = MediaJobSnapshot.CreateQueued(MediaJobId.New(), new FakeDelayJobDefinition { Label = "a" }, t0);
        var second = MediaJobSnapshot.CreateQueued(
            MediaJobId.New(),
            new FakeDelayJobDefinition { Label = "b" },
            t0.AddMilliseconds(1));
        await _fx.Store.InsertAsync(first);
        await _fx.Store.InsertAsync(second);

        var options = new MediaJobClaimOptions("owner-a", TimeSpan.FromSeconds(30), MaxConcurrency: 1);
        var claimed1 = await _fx.Store.TryClaimNextAsync(options, t0.AddSeconds(1));
        var claimed2 = await _fx.Store.TryClaimNextAsync(options, t0.AddSeconds(1));

        Assert.NotNull(claimed1);
        Assert.Equal(first.Id, claimed1.Id);
        Assert.Equal(MediaJobState.Running, claimed1.State);
        Assert.Equal("owner-a", claimed1.LeaseOwner);
        Assert.Null(claimed2);
    }

    [Fact]
    public async Task Heartbeat_requires_matching_lease_owner()
    {
        var now = DateTimeOffset.UtcNow;
        var job = MediaJobSnapshot.CreateQueued(MediaJobId.New(), new FakeDelayJobDefinition(), now);
        await _fx.Store.InsertAsync(job);
        var claimed = await _fx.Store.TryClaimNextAsync(
            new MediaJobClaimOptions("owner-a", TimeSpan.FromSeconds(30), 1),
            now.AddSeconds(1));
        Assert.NotNull(claimed);

        Assert.False(await _fx.Store.HeartbeatAsync(claimed.Id, "other-owner", now.AddSeconds(2), TimeSpan.FromSeconds(30)));
        Assert.True(await _fx.Store.HeartbeatAsync(claimed.Id, "owner-a", now.AddSeconds(2), TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Repair_skips_when_job_lock_still_held()
    {
        var now = DateTimeOffset.UtcNow;
        var job = MediaJobSnapshot.CreateQueued(MediaJobId.New(), new FakeDelayJobDefinition(), now);
        await _fx.Store.InsertAsync(job);
        var claimed = await _fx.Store.TryClaimNextAsync(
            new MediaJobClaimOptions("owner-a", TimeSpan.FromMilliseconds(1), 1),
            now);
        Assert.NotNull(claimed);

        await using var held = await _fx.JobLock.TryAcquireAsync(claimed.Id);
        Assert.NotNull(held);

        var repairedWhileHeld = await _fx.Store.RepairExpiredLeasesAsync(
            now.AddSeconds(5),
            (id, owner) => _fx.JobLock.IsHeld(id, owner));
        Assert.Equal(0, repairedWhileHeld);

        var stillRunning = await _fx.Store.GetAsync(claimed.Id);
        Assert.Equal(MediaJobState.Running, stillRunning!.State);

        await held!.DisposeAsync();

        var repairedAfterRelease = await _fx.Store.RepairExpiredLeasesAsync(
            now.AddSeconds(5),
            (id, owner) => _fx.JobLock.IsHeld(id, owner));
        Assert.Equal(1, repairedAfterRelease);

        var interrupted = await _fx.Store.GetAsync(claimed.Id);
        Assert.Equal(MediaJobState.Interrupted, interrupted!.State);
    }

    [Fact]
    public async Task Expired_lease_with_held_lock_still_counts_against_concurrency()
    {
        var now = DateTimeOffset.UtcNow;
        var running = MediaJobSnapshot.CreateQueued(MediaJobId.New(), new FakeDelayJobDefinition(), now)
            .WithState(MediaJobState.Running, now, startedAt: now) with
            {
                LeaseOwner = "owner-a",
                LeaseAcquiredAt = now.AddSeconds(-60),
                LeaseExpiresAt = now.AddSeconds(-30),
                HeartbeatAt = now.AddSeconds(-60),
            };
        await _fx.Store.InsertAsync(running);

        await using var held = await _fx.JobLock.TryAcquireAsync(running.Id);
        Assert.NotNull(held);

        // Repair must leave the job Running while the lock is held.
        var repaired = await _fx.Store.RepairExpiredLeasesAsync(
            now,
            (id, owner) => _fx.JobLock.IsHeld(id, owner));
        Assert.Equal(0, repaired);

        var queued = MediaJobSnapshot.CreateQueued(
            MediaJobId.New(),
            new FakeDelayJobDefinition { Label = "should-wait" },
            now);
        await _fx.Store.InsertAsync(queued);

        var claimed = await _fx.Store.TryClaimNextAsync(
            new MediaJobClaimOptions("owner-b", TimeSpan.FromSeconds(30), MaxConcurrency: 1),
            now);
        Assert.Null(claimed);

        await held!.DisposeAsync();
        repaired = await _fx.Store.RepairExpiredLeasesAsync(
            now,
            (id, owner) => _fx.JobLock.IsHeld(id, owner));
        Assert.Equal(1, repaired);

        claimed = await _fx.Store.TryClaimNextAsync(
            new MediaJobClaimOptions("owner-b", TimeSpan.FromSeconds(30), MaxConcurrency: 1),
            now);
        Assert.NotNull(claimed);
        Assert.Equal(queued.Id, claimed.Id);
    }

    [Fact]
    public async Task Cancel_after_claim_moves_running_to_canceling()
    {
        var now = DateTimeOffset.UtcNow;
        var job = MediaJobSnapshot.CreateQueued(MediaJobId.New(), new FakeDelayJobDefinition(), now);
        await _fx.Store.InsertAsync(job);

        var claimed = await _fx.Store.TryClaimNextAsync(
            new MediaJobClaimOptions("owner-a", TimeSpan.FromSeconds(30), 1),
            now);
        Assert.NotNull(claimed);

        var outcome = await _fx.Store.CancelAsync(job.Id, now.AddMilliseconds(1));
        Assert.True(outcome.Found);
        Assert.True(outcome.StateChanged);
        Assert.Equal(MediaJobState.Canceling, outcome.Snapshot!.State);
        Assert.NotNull(outcome.Snapshot.CancelRequestedAt);

        var terminal = await _fx.Store.TransitionAsync(
            job.Id,
            MediaJobState.Canceling,
            MediaJobState.Canceled,
            now.AddMilliseconds(2),
            completedAt: now.AddMilliseconds(2),
            clearLeaseOwner: "owner-a");
        Assert.True(terminal);

        var afterSuccessCancel = await _fx.Store.CancelAsync(job.Id, now.AddMilliseconds(3));
        Assert.True(afterSuccessCancel.Found);
        Assert.False(afterSuccessCancel.StateChanged);
        Assert.Equal(MediaJobState.Canceled, afterSuccessCancel.Snapshot!.State);
    }

    [Fact]
    public async Task Transition_to_succeeded_cannot_overwrite_canceling()
    {
        var now = DateTimeOffset.UtcNow;
        var job = MediaJobSnapshot.CreateQueued(MediaJobId.New(), new FakeDelayJobDefinition(), now);
        await _fx.Store.InsertAsync(job);
        var claimed = await _fx.Store.TryClaimNextAsync(
            new MediaJobClaimOptions("owner-a", TimeSpan.FromSeconds(30), 1),
            now);
        Assert.NotNull(claimed);

        var cancel = await _fx.Store.CancelAsync(job.Id, now.AddMilliseconds(1));
        Assert.Equal(MediaJobState.Canceling, cancel.Snapshot!.State);

        var succeeded = await _fx.Store.TransitionAsync(
            job.Id,
            MediaJobState.Running,
            MediaJobState.Succeeded,
            now.AddMilliseconds(2),
            completedAt: now.AddMilliseconds(2),
            clearLeaseOwner: "owner-a");
        Assert.False(succeeded);

        var snapshot = await _fx.Store.GetAsync(job.Id);
        Assert.Equal(MediaJobState.Canceling, snapshot!.State);
        Assert.NotNull(snapshot.CancelRequestedAt);
    }

    [Fact]
    public async Task Concurrent_cancel_and_succeed_never_lose_cancel_or_invent_illegal_transition()
    {
        // Stress the conditional UPDATE path: whichever wins, the loser must observe false / no overwrite.
        for (var i = 0; i < 40; i++)
        {
            var now = DateTimeOffset.UtcNow;
            var job = MediaJobSnapshot.CreateQueued(
                MediaJobId.New(),
                new FakeDelayJobDefinition { Label = $"race-{i}" },
                now);
            await _fx.Store.InsertAsync(job);
            var claimed = await _fx.Store.TryClaimNextAsync(
                new MediaJobClaimOptions("owner-a", TimeSpan.FromSeconds(30), 1),
                now);
            Assert.NotNull(claimed);

            var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelTask = Task.Run(async () =>
            {
                await barrier.Task;
                return await _fx.Store.CancelAsync(job.Id, now.AddMilliseconds(1));
            });
            var succeedTask = Task.Run(async () =>
            {
                await barrier.Task;
                return await _fx.Store.TransitionAsync(
                    job.Id,
                    MediaJobState.Running,
                    MediaJobState.Succeeded,
                    now.AddMilliseconds(1),
                    completedAt: now.AddMilliseconds(1),
                    clearLeaseOwner: "owner-a");
            });

            barrier.SetResult();
            await Task.WhenAll(cancelTask, succeedTask);

            var cancel = await cancelTask;
            var succeeded = await succeedTask;
            var final = await _fx.Store.GetAsync(job.Id);
            Assert.NotNull(final);

            Assert.True(
                final.State is MediaJobState.Canceling or MediaJobState.Succeeded,
                $"Unexpected final state {final.State}");

            if (final.State == MediaJobState.Canceling)
            {
                Assert.True(cancel.StateChanged);
                Assert.False(succeeded);
                Assert.NotNull(final.CancelRequestedAt);
                // Free the concurrency slot before the next stress iteration.
                Assert.True(await _fx.Store.TransitionAsync(
                    job.Id,
                    MediaJobState.Canceling,
                    MediaJobState.Canceled,
                    now.AddMilliseconds(2),
                    completedAt: now.AddMilliseconds(2),
                    clearLeaseOwner: "owner-a"));
            }
            else
            {
                Assert.True(succeeded);
                // Cancel either lost the race (terminal Succeeded → no state change) or never saw Running.
                Assert.False(cancel.StateChanged);
            }
        }
    }

    [Fact]
    public async Task Migration_lock_is_separate_from_job_lock()
    {
        var jobId = MediaJobId.New();
        await using var jobLock = await _fx.JobLock.TryAcquireAsync(jobId);
        Assert.NotNull(jobLock);

        var migrationLock = new FileMigrationLock(Path.Combine(_fx.LockDirectory, "migrate.lock"));
        await using var migrateHandle = await migrationLock.AcquireAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(migrateHandle);
    }
}
