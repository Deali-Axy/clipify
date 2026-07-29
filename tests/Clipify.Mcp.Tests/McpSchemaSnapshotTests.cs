using System.Text.Json;
using System.Text.Json.Nodes;
using Clipify.Application.Jobs;
using Clipify.Application.Media;
using Clipify.Domain.Jobs;
using Clipify.Domain.Media;
using Clipify.Mcp.Security;
using Clipify.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

namespace Clipify.Mcp.Tests;

public class McpSchemaSnapshotTests
{
    private static readonly string[] ExpectedTools =
    [
        "probe_media",
        "trim_video",
        "extract_audio",
        "generate_thumbnail",
        "list_jobs",
        "get_job",
        "wait_job",
        "cancel_job",
        "retry_job",
    ];

    [Fact]
    public void Tool_names_descriptions_and_annotations_match_snapshot()
    {
        using var provider = BuildToolProvider();
        var tools = provider.GetServices<McpServerTool>().OrderBy(t => t.ProtocolTool.Name, StringComparer.Ordinal).ToArray();

        Assert.Equal(ExpectedTools.OrderBy(x => x, StringComparer.Ordinal), tools.Select(t => t.ProtocolTool.Name));

        var snapshot = new JsonObject();
        foreach (var tool in tools)
        {
            var protocol = tool.ProtocolTool;
            var annotations = protocol.Annotations;
            snapshot[protocol.Name] = new JsonObject
            {
                ["title"] = protocol.Title,
                ["description"] = protocol.Description,
                ["readOnlyHint"] = annotations?.ReadOnlyHint,
                ["destructiveHint"] = annotations?.DestructiveHint,
                ["idempotentHint"] = annotations?.IdempotentHint,
                ["openWorldHint"] = annotations?.OpenWorldHint,
                ["required"] = RequiredProperties(protocol.InputSchema),
                ["properties"] = PropertyTypes(protocol.InputSchema),
            };
        }

        var actual = snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var expectedPath = FindSnapshotPath();
        Directory.CreateDirectory(Path.GetDirectoryName(expectedPath)!);

        if (!File.Exists(expectedPath) || string.Equals(Environment.GetEnvironmentVariable("CLIPIFY_UPDATE_SNAPSHOTS"), "1", StringComparison.Ordinal))
        {
            File.WriteAllText(expectedPath, actual + Environment.NewLine);
        }

        var expected = File.ReadAllText(expectedPath).Replace("\r\n", "\n");
        Assert.Equal(expected.TrimEnd(), actual.Replace("\r\n", "\n").TrimEnd());
    }

    [Fact]
    public void Media_tools_are_marked_destructive_and_non_idempotent()
    {
        using var provider = BuildToolProvider();
        var tools = provider.GetServices<McpServerTool>().ToDictionary(t => t.ProtocolTool.Name, StringComparer.Ordinal);

        foreach (var name in new[] { "trim_video", "extract_audio", "generate_thumbnail" })
        {
            var annotations = tools[name].ProtocolTool.Annotations;
            Assert.False(annotations?.ReadOnlyHint);
            Assert.True(annotations?.DestructiveHint);
            Assert.False(annotations?.IdempotentHint);
            Assert.False(annotations?.OpenWorldHint);
        }

        Assert.True(tools["probe_media"].ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.True(tools["cancel_job"].ProtocolTool.Annotations?.DestructiveHint);
        Assert.False(tools["retry_job"].ProtocolTool.Annotations?.IdempotentHint);
    }

    private static ServiceProvider BuildToolProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new McpRuntimeOptions
        {
            AllowedRoots = [Path.GetTempPath()],
            EnableFileLogging = false,
        });
        services.AddSingleton(AllowedRootPolicy.Create([Path.GetTempPath()]));
        services.AddSingleton<IMediaJobService, FakeJobs>();
        services.AddSingleton<IProbeMediaUseCase, FakeProbe>();
        services.AddSingleton<ClipifyMcpTools>();
        services.AddMcpServer().WithTools<ClipifyMcpTools>();
        return services.BuildServiceProvider();
    }

    private static JsonArray RequiredProperties(JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("required", out var required)
            && required.ValueKind == JsonValueKind.Array)
        {
            return new JsonArray(required.EnumerateArray().Select(e => (JsonNode?)e.GetString()).ToArray());
        }

        return [];
    }

    private static JsonObject PropertyTypes(JsonElement schema)
    {
        var result = new JsonObject();
        if (schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in properties.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            result[property.Name] = property.Value.TryGetProperty("type", out var type)
                ? type.ToString()
                : property.Value.ToString();
        }

        return result;
    }

    private static string FindSnapshotPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "Clipify.Mcp.Tests", "Snapshots", "tool-schema.json");
            if (dir.GetDirectories("tests").Length > 0 || File.Exists(candidate))
            {
                return candidate;
            }

            // Prefer repo-root relative when walking up from bin/
            var alt = Path.Combine(dir.FullName, "Snapshots", "tool-schema.json");
            if (dir.Name == "Clipify.Mcp.Tests")
            {
                return alt;
            }

            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "Snapshots", "tool-schema.json");
    }

    private sealed class FakeJobs : IMediaJobService
    {
        public ValueTask<MediaJobId> EnqueueAsync(MediaJobDefinition definition, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(MediaJobId.New());

        public ValueTask RequestCancelAsync(MediaJobId jobId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<MediaJobId> RetryAsync(MediaJobId jobId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(MediaJobId.New());

        public ValueTask<MediaJobSnapshot?> GetAsync(MediaJobId jobId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<MediaJobSnapshot?>(null);

        public ValueTask<IReadOnlyList<MediaJobSnapshot>> ListAsync(MediaJobListQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<MediaJobSnapshot>>([]);

        public ValueTask<IReadOnlyList<MediaArtifact>> ListArtifactsAsync(MediaJobId jobId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<MediaArtifact>>([]);

        public async IAsyncEnumerable<MediaJobChange> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed class FakeProbe : IProbeMediaUseCase
    {
        public Task<Application.Abstractions.Result<MediaInfo>> ExecuteAsync(string inputPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(Application.Abstractions.Result<MediaInfo>.Failure(
                Application.Abstractions.ClipifyError.Validation("unused")));
    }
}
