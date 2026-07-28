namespace Clipify.Domain.Media;

/// <summary>
/// Limited thumbnail image formats. Raw FFmpeg codec/format strings are not accepted.
/// </summary>
public enum ThumbnailImageFormat
{
    Jpg = 0,
    Png = 1,
}
