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
    public bool FailArtifactWrite { get; set; }
    public bool FailCompletedProgress { get; set; }

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

        if (FailArtifactWrite)
        {
            throw new InvalidOperationException("Simulated artifact write failure after commit.");
        }

        await context.AddArtifactAsync(
                MediaArtifact.Create(
                    context.Snapshot.Id,
                    kind: "fake_output",
                    path: $"fake://{context.Snapshot.Id}/barrier",
                    createdAt: _timeProvider.GetUtcNow(),
                    sizeBytes: 0),
                CancellationToken.None)
            .ConfigureAwait(false);

        if (FailCompletedProgress)
        {
            throw new InvalidOperationException("Simulated completed progress failure after commit.");
        }

        await context.ReportProgressAsync(
                MediaJobProgress.Create("completed", _timeProvider.GetUtcNow(), fraction: 1),
                CancellationToken.None)
            .ConfigureAwait(false);

        if (FailAfterCommit)
        {
            throw new InvalidOperationException("Simulated post-commit failure.");
        }

        await AllowReturn.Task.ConfigureAwait(false);
    }
}

public class MediaJobCommitBoundaryTests
{
    private static (
        MediaJobService Service,
        InMemoryMediaJobStore Store,
        MediaJobExecutor Executor,
        BarrierJobHandler Handler) CreateSystem(BarrierJobHandler? handler = null)
    {
        var store = new InMemoryMediaJobStore();
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
        return (service, store, executor, barrier);
    }

    [Fact]
    public async Task Cancel_after_commit_before_handler_returns_still_succeeds()
    {
        var (service, store, executor, handler) = CreateSystem();

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
        var (service, store, executor, handler) = CreateSystem();

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
        var (service, store, executor, _) = CreateSystem(handler);

        var id = await service.EnqueueAsync(new FakeDelayJobDefinition { Delay = TimeSpan.Zero });
        _ = executor.PumpAsync();

        await handler.ReadyBeforeCommit.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handler.AllowCommit.TrySetResult();

        await WaitForStateAsync(store, id, MediaJobState.Succeeded);
        Assert.Single(await store.ListArtifactsAsync(id));
    }

    [Fact]
    public async Task Artifact_write_failure_after_mark_still_succeeds()
    {
        var handler = new BarrierJobHandler { FailArtifactWrite = true };
        handler.AllowReturn.TrySetResult();
        var (service, store, executor, _) = CreateSystem(handler);

        var id = await service.EnqueueAsync(new FakeDelayJobDefinition { Delay = TimeSpan.Zero });
        _ = executor.PumpAsync();

        await handler.ReadyBeforeCommit.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handler.AllowCommit.TrySetResult();

        await WaitForStateAsync(store, id, MediaJobState.Succeeded);
        // Mark happened before artifact write simulation; artifact may be absent.
        Assert.True(handler.Committed.Task.IsCompletedSuccessfully);
    }

    private static async Task WaitForStateAsync(
        InMemoryMediaJobStore store,
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
