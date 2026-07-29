using System.Diagnostics;
using System.Text.Json;
using Clipify.Application.Jobs;
using Clipify.Domain.Jobs;
using Clipify.Hosting;
using Clipify.Mcp.Dto;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clipify.Mcp.Tests;

public class McpStdioProtocolTests
{
    [Fact]
    public async Task Stdio_client_discovers_nine_tools_and_completes_job_flow()
    {
        using var workspace = new TempWorkspace();
        await McpTestHost.EnsureMediaFixtureAsync();
        var input = McpTestHost.CopySourceInto(workspace);
        var output = Path.Combine(workspace.MediaRoot, "stdio-trim.mp4");

        await using var client = await CreateClientAsync(workspace);

        var tools = await client.ListToolsAsync();
        var names = tools.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[]
            {
                "cancel_job",
                "extract_audio",
                "generate_thumbnail",
                "get_job",
                "list_jobs",
                "probe_media",
                "retry_job",
                "trim_video",
                "wait_job",
            },
            names);

        var probe = await CallAsync(client, "probe_media", new Dictionary<string, object?> { ["input"] = input });
        Assert.True(probe.Ok);

        var trim = await CallAsync(client, "trim_video", new Dictionary<string, object?>
        {
            ["input"] = input,
            ["output"] = output,
            ["start"] = "0",
            ["end"] = "500",
        });
        Assert.True(trim.Ok);
        Assert.False(string.IsNullOrWhiteSpace(trim.JobId));

        var wait = await CallAsync(client, "wait_job", new Dictionary<string, object?>
        {
            ["job_id"] = trim.JobId,
            ["timeout_seconds"] = 60,
        });
        Assert.True(wait.Ok);
        Assert.Equal("succeeded", wait.State);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public async Task Stdout_protocol_roundtrip_does_not_surface_log_noise_as_tool_payload()
    {
        using var workspace = new TempWorkspace();
        await McpTestHost.EnsureMediaFixtureAsync();

        await using var client = await CreateClientAsync(workspace);
        var tools = await client.ListToolsAsync();
        Assert.Equal(9, tools.Count);

        // Tool payloads are structured JSON envelopes, not free-form log lines.
        var list = await CallAsync(client, "list_jobs", new Dictionary<string, object?>());
        Assert.True(list.Ok);
        Assert.Null(list.Error);
    }

    [Fact]
    public async Task Shared_database_is_not_double_executed_across_mcp_and_host()
    {
        using var workspace = new TempWorkspace();
        await McpTestHost.EnsureMediaFixtureAsync();

        var optionsA = new ClipifyHostOptions
        {
            DataDirectory = workspace.DataDirectory,
            LeaseOwner = "mcp-a",
            SuppressConsoleLogging = true,
            EnableFileLogging = false,
            PollInterval = TimeSpan.FromMilliseconds(100),
        };
        var optionsB = new ClipifyHostOptions
        {
            DataDirectory = workspace.DataDirectory,
            LeaseOwner = "cli-b",
            SuppressConsoleLogging = true,
            EnableFileLogging = false,
            PollInterval = TimeSpan.FromMilliseconds(100),
        };

        using var hostA = await ClipifyHostFactory.StartAsync(optionsA);
        using var hostB = await ClipifyHostFactory.StartAsync(optionsB);
        try
        {
            var jobs = hostA.Services.GetRequiredService<IMediaJobService>();
            var jobId = await jobs.EnqueueAsync(new FakeDelayJobDefinition
            {
                Delay = TimeSpan.FromMilliseconds(400),
                Label = "dual-mcp",
            });

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
            Assert.Equal(MediaJobState.Succeeded, terminal!.State);
            var listed = await jobs.ListAsync(new MediaJobListQuery(Take: 50));
            Assert.Equal(1, listed.Count(j => j.Id == jobId && j.State == MediaJobState.Succeeded));
        }
        finally
        {
            await hostA.StopAsync();
            await hostB.StopAsync();
        }
    }

    private static async Task<McpClient> CreateClientAsync(TempWorkspace workspace)
    {
        var dll = FindBuiltDll();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "clipify-mcp-test",
            Command = "dotnet",
            Arguments =
            [
                dll,
                "--data-dir", workspace.DataDirectory,
                "--allow-root", workspace.MediaRoot,
                "--no-file-log",
            ],
            WorkingDirectory = workspace.MediaRoot,
        });

        return await McpClient.CreateAsync(transport);
    }

    private static async Task<McpToolResponse> CallAsync(
        McpClient client,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(toolName, arguments);
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        if (result.IsError is true)
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' returned IsError. Content: {text ?? "(none)"}");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Tool returned no text content.");
        }

        return McpTestHost.ParseResponse(text);
    }

    private static string FindBuiltDll()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var config in new[] { "Release", "Debug" })
            {
                var candidate = Path.Combine(dir.FullName, "src", "Clipify.Mcp", "bin", config, "net10.0", "clipify-mcp.dll");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("clipify-mcp.dll not found. Build Clipify.Mcp first.");
    }
}
