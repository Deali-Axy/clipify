namespace Clipify.Domain.Media;

/// <summary>
/// Clipify-owned stream metadata mapped from ffprobe. No ffprobe DTO types.
/// </summary>
public sealed record MediaStreamInfo(
    int Index,
    string CodecType,
    string? CodecName,
    int? Width,
    int? Height,
    double? FrameRate,
    int? SampleRate,
    int? Channels,
    TimeSpan? Duration,
    long? BitRate,
    string? Language);
