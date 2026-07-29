using Clipify.FFmpeg;

namespace Clipify.Mcp;

/// <summary>
/// Parsed MCP host options. Path roots are resolved and validated before the host starts.
/// </summary>
public sealed class McpRuntimeOptions
{
    public string? DataDirectory { get; init; }

    public required IReadOnlyList<string> AllowedRoots { get; init; }

    public Action<ClipifyFFmpegOptions>? ConfigureFFmpeg { get; init; }

    public TimeSpan? PollInterval { get; init; }

    public bool EnableFileLogging { get; init; } = true;

    public string? LeaseOwner { get; init; }
}
