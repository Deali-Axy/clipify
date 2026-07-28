namespace Clipify.Domain.Media;

/// <summary>
/// Limited audio extract formats. Raw FFmpeg codec/format strings are not accepted.
/// </summary>
public enum AudioOutputFormat
{
    Copy = 0,
    Mp3 = 1,
    Aac = 2,
    Wav = 3,
}
