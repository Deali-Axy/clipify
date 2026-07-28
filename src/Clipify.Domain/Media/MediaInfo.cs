namespace Clipify.Domain.Media;

/// <summary>
/// Clipify-owned media container metadata. Domain must not reference Process or ffprobe DTOs.
/// </summary>
public sealed record MediaInfo(
    string Path,
    TimeSpan? Duration,
    long? SizeBytes,
    string? FormatName,
    string? FormatLongName,
    long? BitRate,
    IReadOnlyList<MediaStreamInfo> Streams)
{
    public IEnumerable<MediaStreamInfo> VideoStreams =>
        Streams.Where(s => string.Equals(s.CodecType, "video", StringComparison.OrdinalIgnoreCase));

    public IEnumerable<MediaStreamInfo> AudioStreams =>
        Streams.Where(s => string.Equals(s.CodecType, "audio", StringComparison.OrdinalIgnoreCase));
}
