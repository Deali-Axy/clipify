using Clipify.Application.Jobs;
using Clipify.Domain.Jobs;
using Clipify.Hosting;
using Clipify.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clipify.Persistence.Tests;

public class DualHostIntegrationTests
{
    [Fact]
    public async Task Two_hosts_do_not_execute_same_job_twice()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipify-tests", Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(root, "clipify.db");
        var lockDir = Path.Combine(root, "locks");
        Directory.CreateDirectory(root);

        try
        {
            using var hostA = BuildHost(dbPath, lockDir, "host-a", maxConcurrency: 1);
            using var hostB = BuildHost(dbPath, lockDir, "host-b", maxConcurrency: 1);

            await hostA.Services.MigrateClipifyDatabaseAsync();

            await hostA.StartAsync();
            await hostB.StartAsync();

            var service = hostA.Services.GetRequiredService<IMediaJobService>();
            var jobId = await service.EnqueueAsync(new FakeDelayJobDefinition
            {
                Delay = TimeSpan.FromMilliseconds(200),
                Label = "shared",
            });

            await WaitForAsync(async () =>
            {
                var snapshot = await service.GetAsync(jobId);
                return snapshot?.State == MediaJobState.Succeeded;
            }, TimeSpan.FromSeconds(10));

            var final = await service.GetAsync(jobId);
            Assert.NotNull(final);
            Assert.Equal(MediaJobState.Succeeded, final.State);

            // Exactly one host should have owned the lease historically; artifacts once.
            var artifacts = await service.ListArtifactsAsync(jobId);
            Assert.Single(artifacts);

            await hostA.StopAsync();
            await hostB.StopAsync();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Concurrent_limit_two_allows_two_running_jobs()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipify-tests", Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(root, "clipify.db");
        var lockDir = Path.Combine(root, "locks");
        Directory.CreateDirectory(root);

        try
        {
            using var host = BuildHost(dbPath, lockDir, "host-c2", maxConcurrency: 2);
            await host.Services.MigrateClipifyDatabaseAsync();
            await host.StartAsync();

            var service = host.Services.GetRequiredService<IMediaJobService>();
            var store = host.Services.GetRequiredService<IMediaJobStore>();

            var ids = new List<MediaJobId>();
            for (var i = 0; i < 2; i++)
            {
                ids.Add(await service.EnqueueAsync(new FakeDelayJobDefinition
                {
                    Delay = TimeSpan.FromMilliseconds(400),
                    Label = $"c2-{i}",
                }));
            }

            await WaitForAsync(async () =>
            {
                var running = 0;
                foreach (var id in ids)
                {
                    var snap = await store.GetAsync(id);
                    if (snap?.State is MediaJobState.Running or MediaJobState.Succeeded)
                    {
                        running++;
                    }
                }

                return running == 2;
            }, TimeSpan.FromSeconds(5));

            foreach (var id in ids)
            {
                await WaitForAsync(async () =>
                {
                    var snap = await service.GetAsync(id);
                    return snap?.State == MediaJobState.Succeeded;
                }, TimeSpan.FromSeconds(10));
            }

            await host.StopAsync();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Restart_reloads_queued_and_does_not_autorun_interrupted()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipify-tests", Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(root, "clipify.db");
        var lockDir = Path.Combine(root, "locks");
        Directory.CreateDirectory(root);

        try
        {
            MediaJobId queuedId;
            MediaJobId interruptedId;

            using (var host1 = BuildHost(dbPath, lockDir, "host-restart-1", maxConcurrency: 1))
            {
                await host1.Services.MigrateClipifyDatabaseAsync();
                var store = host1.Services.GetRequiredService<IMediaJobStore>();
                var now = DateTimeOffset.UtcNow;

                queuedId = MediaJobId.New();
                await store.InsertAsync(MediaJobSnapshot.CreateQueued(
                    queuedId,
                    new FakeDelayJobDefinition { Delay = TimeSpan.Zero, Label = "queued-survive" },
                    now));

                interruptedId = MediaJobId.New();
                var running = MediaJobSnapshot.CreateQueued(
                        interruptedId,
                        new FakeDelayJobDefinition { Delay = TimeSpan.FromSeconds(30), Label = "was-running" },
                        now)
                    .WithState(MediaJobState.Running, now, startedAt: now) with
                    {
                        LeaseOwner = "dead-host",
                        LeaseAcquiredAt = now.AddMinutes(-2),
                        LeaseExpiresAt = now.AddMinutes(-1),
                        HeartbeatAt = now.AddMinutes(-2),
                    };
                await store.InsertAsync(running);
            }

            using var host2 = BuildHost(dbPath, lockDir, "host-restart-2", maxConcurrency: 1);
            await host2.Services.MigrateClipifyDatabaseAsync();
            await host2.StartAsync();

            var service = host2.Services.GetRequiredService<IMediaJobService>();

            await WaitForAsync(async () =>
            {
                var snap = await service.GetAsync(queuedId);
                return snap?.State == MediaJobState.Succeeded;
            }, TimeSpan.FromSeconds(10));

            // Give repair a chance to run via worker pump.
            await WaitForAsync(async () =>
            {
                var snap = await service.GetAsync(interruptedId);
                return snap?.State == MediaJobState.Interrupted;
            }, TimeSpan.FromSeconds(10));

            var interrupted = await service.GetAsync(interruptedId);
            Assert.Equal(MediaJobState.Interrupted, interrupted!.State);

            // Interrupted must not auto-rerun.
            await Task.Delay(300);
            interrupted = await service.GetAsync(interruptedId);
            Assert.Equal(MediaJobState.Interrupted, interrupted!.State);

            await host2.StopAsync();
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static IHost BuildHost(string dbPath, string lockDir, string leaseOwner, int maxConcurrency)
    {
        return new HostBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .ConfigureServices(services =>
            {
                services.AddClipifyMediaJobs(new MediaJobHostingOptions
                {
                    DatabasePath = dbPath,
                    LockDirectory = lockDir,
                    LeaseOwner = leaseOwner,
                    MaxConcurrency = maxConcurrency,
                    LeaseDuration = TimeSpan.FromSeconds(15),
                    HeartbeatInterval = TimeSpan.FromMilliseconds(100),
                    PollInterval = TimeSpan.FromMilliseconds(100),
                });
            })
            .Build();
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

            await Task.Delay(50);
        }

        Assert.Fail($"Condition not met within {timeout}.");
    }

    private static void TryDelete(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
