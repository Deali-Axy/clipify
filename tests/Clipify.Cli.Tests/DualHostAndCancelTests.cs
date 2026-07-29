using Clipify.Application.Jobs;
using Clipify.Domain.Jobs;
using Clipify.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Clipify.Cli.Tests;

public class DualHostAndCancelTests
{
    [Fact]
    public async Task Two_hosts_claim_same_job_only_once()
    {
        using var data = new TempDataDirectory();
        var optionsA = new ClipifyHostOptions
        {
            DataDirectory = data.Path,
            LeaseOwner = "cli-a",
            SuppressConsoleLogging = true,
            EnableFileLogging = false,
            PollInterval = TimeSpan.FromMilliseconds(100),
            MaxConcurrency = 1,
        };
        var optionsB = new ClipifyHostOptions
        {
            DataDirectory = data.Path,
            LeaseOwner = "cli-b",
            SuppressConsoleLogging = true,
            EnableFileLogging = false,
            PollInterval = TimeSpan.FromMilliseconds(100),
            MaxConcurrency = 1,
        };

        using var hostA = await ClipifyHostFactory.StartAsync(optionsA);
        using var hostB = await ClipifyHostFactory.StartAsync(optionsB);

        try
        {
            var jobsA = hostA.Services.GetRequiredService<IMediaJobService>();
            var jobId = await jobsA.EnqueueAsync(new FakeDelayJobDefinition
            {
                Delay = TimeSpan.FromMilliseconds(400),
                Label = "dual-host",
            });

            MediaJobSnapshot? terminal = null;
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                terminal = await jobsA.GetAsync(jobId);
                if (terminal?.IsTerminal == true)
                {
                    break;
                }

                await Task.Delay(50);
            }

            Assert.NotNull(terminal);
            Assert.Equal(MediaJobState.Succeeded, terminal!.State);

            // Exactly one lease owner should have completed it; the other host observes the same snapshot.
            var fromB = await hostB.Services.GetRequiredService<IMediaJobService>().GetAsync(jobId);
            Assert.NotNull(fromB);
            Assert.Equal(terminal.Id, fromB!.Id);
            Assert.Equal(MediaJobState.Succeeded, fromB.State);

            // Shared SQLite history: list from either host shows a single row for this id.
            var listed = await jobsA.ListAsync(new MediaJobListQuery(Take: 50));
            Assert.Equal(1, listed.Count(j => j.Id == jobId && j.State == MediaJobState.Succeeded));
        }
        finally
        {
            await hostA.StopAsync();
            await hostB.StopAsync();
        }
    }

    [Fact]
    public async Task Jobs_cancel_stops_running_fake_delay_job()
    {
        using var data = new TempDataDirectory();
        var options = new ClipifyHostOptions
        {
            DataDirectory = data.Path,
            SuppressConsoleLogging = true,
            EnableFileLogging = false,
            PollInterval = TimeSpan.FromMilliseconds(100),
        };

        using var host = await ClipifyHostFactory.StartAsync(options);
        try
        {
            var jobs = host.Services.GetRequiredService<IMediaJobService>();
            var jobId = await jobs.EnqueueAsync(new FakeDelayJobDefinition
            {
                Delay = TimeSpan.FromSeconds(20),
                Label = "cancel-me",
            });

            // Wait until running (or already cancelable queued).
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var snap = await jobs.GetAsync(jobId);
                if (snap?.State is MediaJobState.Running or MediaJobState.Queued)
                {
                    break;
                }

                await Task.Delay(50);
            }

            using var writers = new CapturingWriters();
            var code = await CliTestHost.RunAsync(
                ["jobs", "cancel", jobId.Value, "--json", "--data-dir", data.Path],
                data,
                writers);
            Assert.Equal(CliExitCode.Success, code);

            deadline = DateTime.UtcNow.AddSeconds(15);
            MediaJobSnapshot? terminal = null;
            while (DateTime.UtcNow < deadline)
            {
                terminal = await jobs.GetAsync(jobId);
                if (terminal?.IsTerminal == true)
                {
                    break;
                }

                await Task.Delay(50);
            }

            Assert.NotNull(terminal);
            Assert.True(
                terminal!.State is MediaJobState.Canceled or MediaJobState.Canceling,
                $"Expected canceled, got {terminal.State}");
            // Allow Canceling→Canceled settle.
            if (terminal.State == MediaJobState.Canceling)
            {
                deadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < deadline)
                {
                    terminal = await jobs.GetAsync(jobId);
                    if (terminal?.State == MediaJobState.Canceled)
                    {
                        break;
                    }

                    await Task.Delay(50);
                }
            }

            Assert.Equal(MediaJobState.Canceled, terminal!.State);
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
