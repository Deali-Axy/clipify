namespace Clipify.FFmpeg;

public sealed class ClipifyFFmpegOptions
{
    /// <summary>Explicit ffmpeg path. When set, takes priority over local tools and PATH.</summary>
    public string? FFmpegPath { get; set; }

    /// <summary>Explicit ffprobe path. When set, takes priority over local tools and PATH.</summary>
    public string? FFprobePath { get; set; }

    /// <summary>
    /// Application-local tools directory searched before PATH (e.g. app bundled tools/).
    /// </summary>
    public string? LocalToolsDirectory { get; set; }

    /// <summary>Grace period before Kill(entireProcessTree) after cancel.</summary>
    public TimeSpan CancelGracePeriod { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Max stderr characters retained in process result summaries.</summary>
    public int StderrLogLimitChars { get; set; } = 16_384;
}
