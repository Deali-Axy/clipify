using Clipify.Application.Jobs;
using Clipify.Domain.Jobs;

namespace Clipify.Application.Tests;

/// <summary>
/// Deterministic barriers for commit-boundary races between Handler and Executor.
/// </summary>
public sealed class BarrierJobHandler : IMediaJobHandler<FakeDelayJobDefinition>
{
    private readonly TimeProvider _timeProvider;

    public BarrierJobHandler(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public TaskCompletionSource ReadyBeforeCommit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource AllowCommit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource AllowReturn { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool FailAfterCommit { get; set; }

    public async Task ExecuteAsync(
        FakeDelayJobDefinition definition,
        MediaJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        ReadyBeforeCommit.TrySetResult();
        await AllowCommit.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        // Irreversible side effect boundary.
        context.MarkOutputCommitted();
        Committed.TrySetResult();

        var artifact = MediaArtifact.Create(
            context.Snapshot.Id,
            kind: "fake_output",
            path: $"fake://{context.Snapshot.Id}/barrier",
            createdAt: _timeProvider.GetUtcNow(),
            sizeBytes: 0);

        await PostCommitFinalizer.FinalizeAsync(context, artifact, totalDuration: null, _timeProvider)
            .ConfigureAwait(false);

        if (FailAfterCommit)
        {
            throw new InvalidOperationException("Simulated post-commit failure.");
        }

        await AllowReturn.Task.ConfigureAwait(false);
    }
}

/// <summary>
/// Delegates to <see cref="InMemoryMediaJobStore"/> while injecting controlled post-commit failures.
/// </summary>
public sealed class FlakyPostCommitStore : IMediaJobStore
{
    private readonly InMemoryMediaJobStore _inner;
    private int _failCompletedProgressRemaining;
    private readonly bool _failArtifactAlways;

    public FlakyPostCommitStore(
        InMemoryMediaJobStore inner,
        int failCompletedProgressTimes = 0,
        bool failArtifactAlways = false)
    {
        _inner = inner;
        _failCompletedProgressRemaining = failCompletedProgressTimes;
        _failArtifactAlways = failArtifactAlways;
    }

    public InMemoryMediaJobStore Inner => _inner;

    public ValueTask InsertAsync(MediaJobSnapshot snapshot, CancellationToken cancellationToken = default)
        => _inner.InsertAsync(snapshot, cancellationToken);

    public ValueTask<MediaJobSnapshot?> GetAsync(MediaJobId jobId, CancellationToken cancellationToken = default)
        => _inner.GetAsync(jobId, cancellationToken);

    public ValueTask<IReadOnlyList<MediaJobSnapshot>> ListAsync(
        MediaJobListQuery query,
        CancellationToken cancellationToken = default)
        => _inner.ListAsync(query, cancellationToken);

    public ValueTask UpdateProgressAsync(
        MediaJobId jobId,
        MediaJobProgress progress,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(progress.Stage, "completed", StringComparison.Ordinal)
            && _failCompletedProgressRemaining > 0)
        {
            _failCompletedProgressRemaining--;
            throw new InvalidOperationException("Simulated completed progress failure.");
        }

        return _inner.UpdateProgressAsync(jobId, progress, cancellationToken);
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
        => _inner.TransitionAsync(
            jobId,
            from,
            to,
            updatedAt,
            errorCode,
            errorMessage,
            stage,
            startedAt,
            completedAt,
            clearLeaseOwner,
            cancellationToken);

    public ValueTask<MediaJobCancelOutcome> CancelAsync(
        MediaJobId jobId,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default)
        => _inner.CancelAsync(jobId, requestedAt, cancellationToken);

    public ValueTask<bool> IsCancelRequestedAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default)
        => _inner.IsCancelRequestedAsync(jobId, cancellationToken);

    public ValueTask<MediaJobSnapshot?> TryClaimNextAsync(
        MediaJobClaimOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
        => _inner.TryClaimNextAsync(options, now, cancellationToken);

    public ValueTask<bool> HeartbeatAsync(
        MediaJobId jobId,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
        => _inner.HeartbeatAsync(jobId, leaseOwner, now, leaseDuration, cancellationToken);

    public ValueTask<int> RepairExpiredLeasesAsync(
        DateTimeOffset now,
        Func<MediaJobId, string?, bool> isJobLockHeldByOwner,
        CancellationToken cancellationToken = default)
        => _inner.RepairExpiredLeasesAsync(now, isJobLockHeldByOwner, cancellationToken);

    public ValueTask AddArtifactAsync(MediaArtifact artifact, CancellationToken cancellationToken = default)
    {
        if (_failArtifactAlways)
        {
            throw new InvalidOperationException("Simulated permanent artifact write failure.");
        }

        return _inner.AddArtifactAsync(artifact, cancellationToken);
    }

    public ValueTask<IReadOnlyList<MediaArtifact>> ListArtifactsAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default)
        => _inner.ListArtifactsAsync(jobId, cancellationToken);
}

public class MediaJobCommitBoundaryTests
{
    private static (
        MediaJobService Service,
        IMediaJobStore Store,
        InMemoryMediaJobStore InnerStore,
        MediaJobExecutor Executor,
        BarrierJobHandler Handler) CreateSystem(
        BarrierJobHandler? handler = null,
        IMediaJobStore? storeOverride = null)
    {
        var inner = storeOverride is FlakyPostCommitStore flaky
            ? flaky.Inner
            : storeOverride as InMemoryMediaJobStore ?? new InMemoryMediaJobStore();
        var store = storeOverride ?? inner;
        var queue = new ChannelMediaJobQueue();
        var changes = new ChannelMediaJobChangePublisher();
        var cancellation = new InMemoryJobCancellationRegistry();
        var jobLock = new InMemoryJobLock();
        var time = TimeProvider.System;
        var barrier = handler ?? new BarrierJobHandler(time);
        var service = new MediaJobService(store, queue, changes, cancellation, time);
        var options = new MediaJobWorkerOptions
        {
            LeaseOwner = "boundary-host",
            MaxConcurrency = 1,
            LeaseDuration = TimeSpan.FromSeconds(30),
            HeartbeatInterval = TimeSpan.FromMilliseconds(50),
            PollInterval = TimeSpan.FromMilliseconds(50),
        };
        var executor = new MediaJobExecutor(
            store,
            queue,
            new MediaJobHandlerDispatcher(barrier),
            cancellation,
            jobLock,
            changes,
            options,
            time);
        return (service, store, inner, executor, barrier);
    }

    [Fact]
    public async Task Cancel_after_commit_before_handler_returns_still_succeeds()
    {
        var (service, store, _, executor, handler) = CreateSystem();

        var id = await service.EnqueueAsync(new FakeDelayJobDefinition { Delay = TimeSpan.Zero, Label = "post-commit-cancel" });
        _ = executor.PumpAsync();

        await handler.ReadyBeforeCommit.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handler.AllowCommit.TrySetResult();
        await handler.Committed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await service.RequestCancelAsync(id);
        var mid = await store.GetAsync(id);
        Assert.Equal(MediaJobState.Canceling, mid?.State);

        handler.AllowReturn.TrySetResult();

        await WaitForStateAsync(store, id, MediaJobState.Succeeded);
        var artifacts = await store.ListArtifactsAsync(id);
        Assert.Single(artifacts);
    }

    [Fact]
    public async Task Cancel_before_commit_cancels_without_artifact()
    {
        var (service, store, _, executor, handler) = CreateSystem();

        var id = await service.EnqueueAsync(new FakeDelayJobDefinition { Delay = TimeSpan.Zero, Label = "pre-commit-cancel" });
        _ = executor.PumpAsync();

        await handler.ReadyBeforeCommit.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Let the handler enter AllowCommit.WaitAsync(cancellationToken) before canceling.
        await Task.Delay(50);
        await service.RequestCancelAsync(id);

        // Do not release AllowCommit — cancel token must abort the wait before MarkOutputCommitted.
        await WaitForStateAsync(store, id, MediaJobState.Canceled);
        Assert.Empty(await store.ListArtifactsAsync(id));
        Assert.False(handler.Committed.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Post_commit_exception_still_succeeds()
    {
        var handler = new BarrierJobHandler { FailAfterCommit = true };
        handler.AllowReturn.TrySetResult();
        var (service, store, _, executor, _) = CreateSystem(handler);

        var id = await service.EnqueueAsync(new FakeDelayJobDefinition { Delay = TimeSpan.Zero });
        _ = executor.PumpAsync();

        await handler.ReadyBeforeCommit.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handler.AllowCommit.TrySetResult();

        await WaitForStateAsync(store, id, MediaJobState.Succeeded);
        Assert.Single(await store.ListArtifactsAsync(id));
    }

    [Fact]
    public async Task Artifact_ok_then_completed_progress_fails_once_yields_single_artifact()
    {
        var inner = new InMemoryMediaJobStore();
        var flaky = new FlakyPostCommitStore(inner, failCompletedProgressTimes: 1);
        var handler = new BarrierJobHandler();
        handler.AllowReturn.TrySetResult();
        var (service, store, _, executor, _) = CreateSystem(handler, flaky);

        var id = await service.EnqueueAsync(new FakeDelayJobDefinition { Delay = TimeSpan.Zero });
        _ = executor.PumpAsync();

        await handler.ReadyBeforeCommit.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handler.AllowCommit.TrySetResult();

        await WaitForStateAsync(store, id, MediaJobState.Succeeded);
        Assert.Single(await store.ListArtifactsAsync(id));

        var snap = await store.GetAsync(id);
        Assert.Null(snap?.ErrorCode);
    }

    [Fact]
    public async Task Permanent_artifact_failure_succeeds_with_post_commit_warning()
    {
        var inner = new InMemoryMediaJobStore();
        var flaky = new FlakyPostCommitStore(inner, failArtifactAlways: true);
        var handler = new BarrierJobHandler();
        handler.AllowReturn.TrySetResult();
        var (service, store, _, executor, _) = CreateSystem(handler, flaky);

        var id = await service.EnqueueAsync(new FakeDelayJobDefinition { Delay = TimeSpan.Zero });
        _ = executor.PumpAsync();

        await handler.ReadyBeforeCommit.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handler.AllowCommit.TrySetResult();

        await WaitForStateAsync(store, id, MediaJobState.Succeeded);
        Assert.Empty(await store.ListArtifactsAsync(id));

        var snap = await store.GetAsync(id);
        Assert.Equal("PostCommitWarning", snap?.ErrorCode);
        Assert.Contains("artifact", snap?.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddArtifact_is_idempotent_by_artifact_id()
    {
        var store = new InMemoryMediaJobStore();
        var jobId = MediaJobId.New();
        var now = DateTimeOffset.UtcNow;
        await store.InsertAsync(
            MediaJobSnapshot.CreateQueued(
                jobId,
                new FakeDelayJobDefinition { Delay = TimeSpan.Zero },
                now));

        var artifact = MediaArtifact.Create(
            jobId,
            kind: "fake_output",
            path: "fake://idempotent",
            createdAt: now,
            artifactId: "stable-artifact-id");

        await store.AddArtifactAsync(artifact);
        await store.AddArtifactAsync(artifact);

        Assert.Single(await store.ListArtifactsAsync(jobId));
    }

    private static async Task WaitForStateAsync(
        IMediaJobStore store,
        MediaJobId id,
        MediaJobState expected,
        TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(5);
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < limit)
        {
            var snap = await store.GetAsync(id);
            if (snap?.State == expected)
            {
                return;
            }

            await Task.Delay(20);
        }

        var final = await store.GetAsync(id);
        Assert.Fail($"Timed out waiting for {expected}; last state={final?.State}");
    }
}
