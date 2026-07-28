using Clipify.FFmpeg;

namespace Clipify.Hosting;

/// <summary>
/// Shared host options for CLI, MCP, and Desktop. Paths default to
/// <see cref="ClipifyAppPaths"/> and may be overridden for tests.
/// </summary>
public sealed class ClipifyHostOptions
{
    /// <summary>
    /// Explicit application data root. When null, uses
    /// <see cref="ClipifyAppPaths.ResolveDefaultRoot"/>.
    /// </summary>
    public string? DataDirectory { get; set; }

    /// <summary>
    /// Unique lease owner for this process. Defaults to a generated value.
    /// </summary>
    public string? LeaseOwner { get; set; }

    public int MaxConcurrency { get; set; } = 1;

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int QueueCapacity { get; set; } = 256;

    /// <summary>
    /// When true, console logging is suppressed so JSON/JSONL stdout stays clean.
    /// File logging under the data directory remains available when enabled.
    /// </summary>
    public bool SuppressConsoleLogging { get; set; }

    /// <summary>
    /// When false, file logging is not added (useful for short-lived tests).
    /// </summary>
    public bool EnableFileLogging { get; set; } = true;

    public Action<ClipifyFFmpegOptions>? ConfigureFFmpeg { get; set; }

    public ClipifyAppPaths ResolvePaths() => ClipifyAppPaths.Create(DataDirectory);

    public string ResolveLeaseOwner() =>
        string.IsNullOrWhiteSpace(LeaseOwner)
            ? $"clipify-{Environment.ProcessId}-{Guid.NewGuid():N}"
            : LeaseOwner;
}
