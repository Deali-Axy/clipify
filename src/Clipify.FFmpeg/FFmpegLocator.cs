using Clipify.Application.Abstractions;
using Clipify.Application.Media;
using Microsoft.Extensions.Options;

namespace Clipify.FFmpeg;

/// <summary>
/// Resolves ffmpeg/ffprobe lazily: explicit config → local tools directory → PATH.
/// </summary>
public sealed class FFmpegLocator : IFFmpegLocator
{
    private readonly ClipifyFFmpegOptions _options;
    private readonly object _gate = new();
    private string? _ffmpegPath;
    private string? _ffprobePath;

    public FFmpegLocator(IOptions<ClipifyFFmpegOptions> options)
    {
        _options = options.Value;
    }

    public string ResolveFFmpegPath()
    {
        lock (_gate)
        {
            return _ffmpegPath ??= ResolveBinary(
                configured: _options.FFmpegPath,
                fileName: OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        }
    }

    public string ResolveFFprobePath()
    {
        lock (_gate)
        {
            return _ffprobePath ??= ResolveBinary(
                configured: _options.FFprobePath,
                fileName: OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        }
    }

    private string ResolveBinary(string? configured, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var explicitPath = Path.GetFullPath(configured);
            if (!File.Exists(explicitPath))
            {
                throw new ClipifyException(
                    ClipifyErrorCode.FfmpegUnavailable,
                    $"Configured binary not found: {explicitPath}");
            }

            return explicitPath;
        }

        if (!string.IsNullOrWhiteSpace(_options.LocalToolsDirectory))
        {
            var local = Path.GetFullPath(Path.Combine(_options.LocalToolsDirectory, fileName));
            if (File.Exists(local))
            {
                return local;
            }
        }

        var fromPath = FindOnPath(fileName);
        if (fromPath is not null)
        {
            return fromPath;
        }

        throw new ClipifyException(
            ClipifyErrorCode.FfmpegUnavailable,
            $"Could not locate '{fileName}'. Set ClipifyFFmpegOptions or install it on PATH.");
    }

    internal static string? FindOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
        {
            return null;
        }

        foreach (var segment in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(segment.Trim().Trim('"'), fileName);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }
}
