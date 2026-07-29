using System.Text.Json;
using Clipify.Application.Jobs;
using Clipify.Domain.Jobs;
using Clipify.Mcp.Dto;
using Clipify.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Clipify.Mcp.Tests;

public class McpToolFlowTests
{
    [Fact]
    public async Task Probe_trim_wait_and_list_work_in_process()
    {
        using var workspace = new TempWorkspace();
        await using var host = await McpTestHost.StartAsync(workspace);
        var tools = host.Services.GetRequiredService<ClipifyMcpTools>();
        var input = McpTestHost.CopySourceInto(workspace);
        var output = Path.Combine(workspace.MediaRoot, "trimmed.mp4");

        var probeJson = await tools.ProbeMedia(input, CancellationToken.None);
        var probe = McpTestHost.ParseResponse(probeJson);
        Assert.True(probe.Ok);
        Assert.NotNull(probe.Result);

        var trimJson = await tools.TrimVideo(
            input,
            output,
            start: "0",
            end: "1000",
            conflict_policy: "fail",
            CancellationToken.None);
        var trim = McpTestHost.ParseResponse(trimJson);
        Assert.True(trim.Ok);
        Assert.False(string.IsNullOrWhiteSpace(trim.JobId));
        Assert.Equal("queued", trim.State);

        var waitJson = await tools.WaitJob(trim.JobId!, timeout_seconds: 60, CancellationToken.None);
        var wait = McpTestHost.ParseResponse(waitJson);
        Assert.True(wait.Ok);
        Assert.Equal("succeeded", wait.State);
        Assert.True(File.Exists(output));

        var listJson = await tools.ListJobs(states: "succeeded", skip: 0, take: 20, CancellationToken.None);
        var list = McpTestHost.ParseResponse(listJson);
        Assert.True(list.Ok);
    }

    [Fact]
    public async Task Cancel_job_does_not_require_waiting_for_ffmpeg()
    {
        using var workspace = new TempWorkspace();
        await using var host = await McpTestHost.StartAsync(workspace);
        var tools = host.Services.GetRequiredService<ClipifyMcpTools>();
        var jobs = host.Services.GetRequiredService<IMediaJobService>();

        var jobId = await jobs.EnqueueAsync(new FakeDelayJobDefinition
        {
            Delay = TimeSpan.FromSeconds(30),
            Label = "mcp-cancel",
        });

        var cancelJson = await tools.CancelJob(jobId.Value, CancellationToken.None);
        var cancel = McpTestHost.ParseResponse(cancelJson);
        Assert.True(cancel.Ok);

        MediaJobSnapshot? terminal = null;
        var deadline = DateTime.UtcNow.AddSeconds(15);
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
            $"Unexpected state {terminal.State}");
    }

    [Fact]
    public async Task Retry_creates_new_job_id()
    {
        using var workspace = new TempWorkspace();
        await using var host = await McpTestHost.StartAsync(workspace);
        var tools = host.Services.GetRequiredService<ClipifyMcpTools>();
        var jobs = host.Services.GetRequiredService<IMediaJobService>();

        var original = await jobs.EnqueueAsync(new FakeDelayJobDefinition
        {
            Delay = TimeSpan.FromMilliseconds(20),
            Fail = true,
            Label = "mcp-retry",
        });

        var waitJson = await tools.WaitJob(original.Value, timeout_seconds: 30, CancellationToken.None);
        var wait = McpTestHost.ParseResponse(waitJson);
        Assert.True(wait.Ok);
        Assert.Equal("failed", wait.State);

        var retryJson = await tools.RetryJob(original.Value, CancellationToken.None);
        var retry = McpTestHost.ParseResponse(retryJson);
        Assert.True(retry.Ok);
        Assert.NotEqual(original.Value, retry.JobId);
    }

    [Fact]
    public async Task Wait_timeout_returns_latest_snapshot_without_failing_job()
    {
        using var workspace = new TempWorkspace();
        await using var host = await McpTestHost.StartAsync(workspace);
        var tools = host.Services.GetRequiredService<ClipifyMcpTools>();
        var jobs = host.Services.GetRequiredService<IMediaJobService>();

        var jobId = await jobs.EnqueueAsync(new FakeDelayJobDefinition
        {
            Delay = TimeSpan.FromSeconds(20),
            Label = "mcp-timeout",
        });

        var waitJson = await tools.WaitJob(jobId.Value, timeout_seconds: 1, CancellationToken.None);
        var wait = McpTestHost.ParseResponse(waitJson);
        Assert.True(wait.Ok);
        Assert.NotEqual("failed", wait.State);

        using var doc = JsonDocument.Parse(waitJson);
        var timedOut = doc.RootElement.GetProperty("result").GetProperty("wait").GetProperty("timed_out").GetBoolean();
        Assert.True(timedOut);

        var stillRunning = await jobs.GetAsync(jobId);
        Assert.NotNull(stillRunning);
        Assert.False(stillRunning!.IsTerminal);
    }

    [Fact]
    public async Task Tool_call_cancel_does_not_cancel_queued_job()
    {
        using var workspace = new TempWorkspace();
        await using var host = await McpTestHost.StartAsync(workspace);
        var tools = host.Services.GetRequiredService<ClipifyMcpTools>();
        var jobs = host.Services.GetRequiredService<IMediaJobService>();

        var jobId = await jobs.EnqueueAsync(new FakeDelayJobDefinition
        {
            Delay = TimeSpan.FromSeconds(15),
            Label = "mcp-tool-cancel",
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await tools.WaitJob(jobId.Value, timeout_seconds: 30, cts.Token));

        var snapshot = await jobs.GetAsync(jobId);
        Assert.NotNull(snapshot);
        Assert.False(snapshot!.State is MediaJobState.Canceled or MediaJobState.Canceling);
    }

    [Fact]
    public async Task Path_outside_allow_root_is_rejected_by_media_tool()
    {
        using var workspace = new TempWorkspace();
        await using var host = await McpTestHost.StartAsync(workspace);
        var tools = host.Services.GetRequiredService<ClipifyMcpTools>();

        var outside = Path.Combine(Path.GetTempPath(), $"clipify-denied-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(outside, [0x00]);
        try
        {
            var json = await tools.ProbeMedia(outside, CancellationToken.None);
            var response = McpTestHost.ParseResponse(json);
            Assert.False(response.Ok);
            Assert.Equal(nameof(Application.Abstractions.ClipifyErrorCode.Validation), response.Error!.Code);
        }
        finally
        {
            try { File.Delete(outside); } catch { /* ignore */ }
        }
    }
}
