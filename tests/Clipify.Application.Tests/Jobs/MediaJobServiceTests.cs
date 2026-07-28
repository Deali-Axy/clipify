using Clipify.Application.Jobs;
using Clipify.Domain.Jobs;

namespace Clipify.Application.Tests;

public class MediaJobServiceTests
{
    private static (MediaJobService Service, InMemoryMediaJobStore Store, ChannelMediaJobQueue Queue, MediaJobExecutor Executor) CreateSystem(
        string leaseOwner = "test-host",
        int maxConcurrency = 1)
    {
        var store = new InMemoryMediaJobStore();
        var queue = new ChannelMediaJobQueue();
        var changes = new ChannelMediaJobChangePublisher();
        var cancellation = new InMemoryJobCancellationRegistry();
        var jobLock = new InMemoryJobLock();
        var time = TimeProvider.System;
        var service = new MediaJobService(store, queue, changes, cancellation, time);
        var options = new MediaJobWorkerOptions
        {
            LeaseOwner = leaseOwner,
            MaxConcurrency = maxConcurrency,
            LeaseDuration = TimeSpan.FromSeconds(30),
            HeartbeatInterval = TimeSpan.FromMilliseconds(50),
            PollInterval = TimeSpan.FromMilliseconds(50),
        };
        var executor = new MediaJobExecutor(
            store,
            queue,
            new MediaJobHandlerDispatcher(new FakeDelayJobHandler(time)),
            cancellation,
            jobLock,
            changes,
            options,
            time);
        return (service, store, queue, executor);
    }

    [Fact]
    public async Task Enqueue_persists_before_wake()
    {
        var (service, store, queue, _) = CreateSystem();
        var persistedBeforeWake = false;

        // Observe that insert completed by reading store before consuming queue.
        var id = await service.EnqueueAsync(new FakeDelayJobDefinition { Label = "persist-first" });
        var snapshot = await store.GetAsync(id);
        persistedBeforeWake = snapshot is not null && snapshot.State == MediaJobState.Queued;

        Assert.True(persistedBeforeWake);
        Assert.True(await queue.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Cancel_queued_job_marks_canceled()
    {
        var (service, store, _, _) = CreateSystem();
        var id = await service.EnqueueAsync(new FakeDelayJobDefinition { Delay = TimeSpan.FromSeconds(30) });

        await service.RequestCancelAsync(id);

        var snapshot = await store.GetAsync(id);
        Assert.NotNull(snapshot);
        Assert.Equal(MediaJobState.Canceled, snapshot.State);
    }

    [Fact]
    public async Task Retry_creates_new_job_linked_to_original()
    {
        var (service, store, _, executor) = CreateSystem();
        var id = await service.EnqueueAsync(new FakeDelayJobDefinition { Fail = true, Delay = TimeSpan.Zero, Label = "boom" });

        await executor.PumpAsync();
        await WaitForStateAsync(store, id, MediaJobState.Failed);

        var retryId = await service.RetryAsync(id);
        var retry = await store.GetAsync(retryId);

        Assert.NotEqual(id, retryId);
        Assert.NotNull(retry);
        Assert.Equal(MediaJobState.Queued, retry.State);
        Assert.Equal(id, retry.RetryOfJobId);
    }

    [Fact]
    public async Task Handler_exception_is_isolated_and_marks_failed()
    {
        var (service, store, _, executor) = CreateSystem();
        var failId = await service.EnqueueAsync(new FakeDelayJobDefinition { Fail = true, Delay = TimeSpan.Zero });
        var okId = await service.EnqueueAsync(new FakeDelayJobDefinition { Delay = TimeSpan.Zero, Label = "ok" });

        await executor.PumpAsync();
        await WaitForStateAsync(store, failId, MediaJobState.Failed);
        await executor.PumpAsync();
        await WaitForStateAsync(store, okId, MediaJobState.Succeeded);

        var ok = await store.GetAsync(okId);
        Assert.Equal(MediaJobState.Succeeded, ok!.State);
    }

    [Fact]
    public async Task Successful_job_exposes_artifact()
    {
        var (service, store, _, executor) = CreateSystem();
        var id = await service.EnqueueAsync(new FakeDelayJobDefinition { Delay = TimeSpan.Zero, Label = "artifact" });

        await executor.PumpAsync();
        await WaitForStateAsync(store, id, MediaJobState.Succeeded);

        var artifacts = await service.ListArtifactsAsync(id);
        Assert.Single(artifacts);
        Assert.Equal("fake_output", artifacts[0].Kind);
    }

    private static async Task WaitForStateAsync(
        IMediaJobStore store,
        MediaJobId id,
        MediaJobState expected,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = await store.GetAsync(id);
            if (snapshot?.State == expected)
            {
                return;
            }

            await Task.Delay(20);
        }

        var final = await store.GetAsync(id);
        Assert.Fail($"Timed out waiting for {id} to become {expected}. Last state: {final?.State}");
    }
}
