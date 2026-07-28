using Clipify.Application.Jobs;
using Clipify.Domain.Jobs;

namespace Clipify.Application.Tests;

public class MediaJobRaceRegressionTests
{
    private static (
        MediaJobService Service,
        InMemoryMediaJobStore Store,
        ChannelMediaJobQueue Queue,
        ChannelMediaJobChangePublisher Changes,
        MediaJobExecutor Executor,
        InMemoryJobLock JobLock) CreateSystem(
        string leaseOwner = "test-host",
        int maxConcurrency = 1,
        TimeSpan? pollInterval = null)
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
            PollInterval = pollInterval ?? TimeSpan.FromMilliseconds(30),
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
        return (service, store, queue, changes, executor, jobLock);
    }

    [Fact]
    public async Task RunAsync_survives_burst_of_channel_wakes()
    {
        var (service, store, queue, _, executor, _) = CreateSystem(pollInterval: TimeSpan.FromMilliseconds(20));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var run = executor.RunAsync(cts.Token);

        // Burst wake signals must not crash the loop with concurrent PeriodicTimer waits.
        for (var i = 0; i < 20; i++)
        {
            var id = await service.EnqueueAsync(new FakeDelayJobDefinition
            {
                Delay = TimeSpan.Zero,
                Label = $"wake-{i}",
            });
            await queue.NotifyAsync(id);
            await Task.Delay(5);
        }

        await WaitForAsync(async () =>
        {
            var list = await store.ListAsync(new MediaJobListQuery(Take: 100));
            return list.Count >= 20 && list.All(j => j.State == MediaJobState.Succeeded);
        }, TimeSpan.FromSeconds(5));

        await cts.CancelAsync();
        try
        {
            await run;
        }
        catch (OperationCanceledException)
        {
        }

        Assert.True(run.IsCompletedSuccessfully || run.IsCanceled);
    }

    [Fact]
    public async Task Cancel_after_claim_sets_cancel_request_instead_of_false_canceled()
    {
        var (service, store, _, changes, _, _) = CreateSystem();
        var id = await service.EnqueueAsync(new FakeDelayJobDefinition
        {
            Delay = TimeSpan.FromSeconds(30),
            Label = "claimed",
        });

        var claimed = await store.TryClaimNextAsync(
            new MediaJobClaimOptions("owner-a", TimeSpan.FromSeconds(30), 1),
            DateTimeOffset.UtcNow);
        Assert.NotNull(claimed);
        Assert.Equal(MediaJobState.Running, claimed.State);

        var watched = new List<MediaJobChange>();
        using var watchCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var watchTask = Task.Run(async () =>
        {
            await foreach (var change in changes.WatchAsync(watchCts.Token))
            {
                watched.Add(change);
                if (change.State is MediaJobState.Canceling or MediaJobState.Canceled)
                {
                    break;
                }
            }
        });

        await service.RequestCancelAsync(id);

        var snapshot = await store.GetAsync(id);
        Assert.NotNull(snapshot);
        Assert.Equal(MediaJobState.Canceling, snapshot.State);
        Assert.NotNull(snapshot.CancelRequestedAt);
        Assert.DoesNotContain(watched, c => c.JobId == id && c.State == MediaJobState.Canceled);

        await watchCts.CancelAsync();
        try
        {
            await watchTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    [Fact]
    public async Task Cancel_after_success_does_not_publish_canceling()
    {
        var (service, store, _, changes, executor, _) = CreateSystem();
        var id = await service.EnqueueAsync(new FakeDelayJobDefinition { Delay = TimeSpan.Zero });

        await executor.PumpAsync();
        await WaitForAsync(async () => (await store.GetAsync(id))?.State == MediaJobState.Succeeded, TimeSpan.FromSeconds(3));

        var cancelingSeen = false;
        using var watchCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var watchTask = Task.Run(async () =>
        {
            await foreach (var change in changes.WatchAsync(watchCts.Token))
            {
                if (change.JobId == id && change.State == MediaJobState.Canceling)
                {
                    cancelingSeen = true;
                }
            }
        });

        await service.RequestCancelAsync(id);

        var snapshot = await store.GetAsync(id);
        Assert.Equal(MediaJobState.Succeeded, snapshot!.State);
        Assert.Null(snapshot.CancelRequestedAt);

        try
        {
            await watchTask;
        }
        catch (OperationCanceledException)
        {
        }

        Assert.False(cancelingSeen);
    }

    [Fact]
    public async Task WatchAsync_broadcasts_to_each_subscriber()
    {
        var changes = new ChannelMediaJobChangePublisher();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var first = new List<MediaJobChange>();
        var second = new List<MediaJobChange>();

        var watch1 = Task.Run(async () =>
        {
            await foreach (var change in changes.WatchAsync(cts.Token))
            {
                first.Add(change);
                if (first.Count >= 2)
                {
                    break;
                }
            }
        });
        var watch2 = Task.Run(async () =>
        {
            await foreach (var change in changes.WatchAsync(cts.Token))
            {
                second.Add(change);
                if (second.Count >= 2)
                {
                    break;
                }
            }
        });

        // Give subscribers time to register.
        await Task.Delay(50);

        var jobId = MediaJobId.New();
        await changes.PublishAsync(new MediaJobChange(jobId, MediaJobState.Queued, DateTimeOffset.UtcNow));
        await changes.PublishAsync(new MediaJobChange(jobId, MediaJobState.Running, DateTimeOffset.UtcNow));

        await Task.WhenAll(watch1, watch2);

        Assert.Equal(2, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Equal(first.Select(c => c.State), second.Select(c => c.State));
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail($"Condition not met within {timeout}.");
    }
}
