using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clipify.Application.Abstractions;
using Clipify.Application.Media;
using Clipify.Domain.Media;
using Microsoft.Extensions.Logging;

namespace Clipify.FFmpeg;

/// <summary>
/// Internal ffprobe JSON DTO — never exposed outside Clipify.FFmpeg.
/// </summary>
internal sealed class FFprobeJsonDto
{
    [JsonPropertyName("streams")]
    public List<FFprobeStreamDto>? Streams { get; set; }

    [JsonPropertyName("format")]
    public FFprobeFormatDto? Format { get; set; }
}

internal sealed class FFprobeFormatDto
{
    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("format_name")]
    public string? FormatName { get; set; }

    [JsonPropertyName("format_long_name")]
    public string? FormatLongName { get; set; }

    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    [JsonPropertyName("size")]
    public string? Size { get; set; }

    [JsonPropertyName("bit_rate")]
    public string? BitRate { get; set; }
}

internal sealed class FFprobeStreamDto
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("codec_type")]
    public string? CodecType { get; set; }

    [JsonPropertyName("codec_name")]
    public string? CodecName { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("avg_frame_rate")]
    public string? AvgFrameRate { get; set; }

    [JsonPropertyName("r_frame_rate")]
    public string? RFrameRate { get; set; }

    [JsonPropertyName("sample_rate")]
    public string? SampleRate { get; set; }

    [JsonPropertyName("channels")]
    public int? Channels { get; set; }

    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    [JsonPropertyName("bit_rate")]
    public string? BitRate { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FFprobeClient : IFFprobeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IFFmpegLocator _locator;
    private readonly ILogger<FFprobeClient> _logger;

    public FFprobeClient(
        IFFmpegLocator locator,
        ILogger<FFprobeClient> logger)
    {
        _locator = locator;
        _logger = logger;
    }

    public async Task<MediaInfo> ProbeAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        var fullPath = Path.GetFullPath(inputPath);

        var args = new[]
        {
            "-v", "error",
            "-show_format",
            "-show_streams",
            "-of", "json",
            fullPath,
        };

        var result = await RunProbeProcessAsync(args, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            _logger.LogWarning(
                "ffprobe failed for {Path} exit={ExitCode} stderr={Stderr}",
                fullPath,
                result.ExitCode,
                result.Stderr);
            throw new ClipifyException(
                ClipifyErrorCode.MediaProbeFailed,
                $"ffprobe failed with exit code {result.ExitCode}.");
        }

        if (string.IsNullOrWhiteSpace(result.Stdout))
        {
            throw new ClipifyException(
                ClipifyErrorCode.MediaProbeFailed,
                "ffprobe returned empty JSON.");
        }

        FFprobeJsonDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<FFprobeJsonDto>(result.Stdout, JsonOptions)
                ?? throw new JsonException("Deserialized to null.");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid ffprobe JSON for {Path}", fullPath);
            throw new ClipifyException(
                ClipifyErrorCode.MediaProbeFailed,
                "ffprobe returned invalid JSON.",
                detail: ex.Message,
                innerException: ex);
        }

        return Map(fullPath, dto);
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunProbeProcessAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = _locator.ResolveFFprobePath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new ClipifyException(
                ClipifyErrorCode.FfmpegUnavailable,
                "Failed to start ffprobe.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return (process.ExitCode, stdout, stderr);
    }

    internal static MediaInfo Map(string path, FFprobeJsonDto dto)
    {
        var format = dto.Format;
        var streams = (dto.Streams ?? [])
            .Select(MapStream)
            .ToArray();

        return new MediaInfo(
            Path: path,
            Duration: ParseDuration(format?.Duration),
            SizeBytes: ParseInt64(format?.Size),
            FormatName: format?.FormatName,
            FormatLongName: format?.FormatLongName,
            BitRate: ParseInt64(format?.BitRate),
            Streams: streams);
    }

    private static MediaStreamInfo MapStream(FFprobeStreamDto stream)
    {
        string? language = null;
        if (stream.Tags is not null
            && stream.Tags.TryGetValue("language", out var lang)
            && !string.IsNullOrWhiteSpace(lang))
        {
            language = lang;
        }

        return new MediaStreamInfo(
            Index: stream.Index,
            CodecType: stream.CodecType ?? "unknown",
            CodecName: stream.CodecName,
            Width: stream.Width,
            Height: stream.Height,
            FrameRate: ParseFrameRate(stream.AvgFrameRate) ?? ParseFrameRate(stream.RFrameRate),
            SampleRate: ParseInt32(stream.SampleRate),
            Channels: stream.Channels,
            Duration: ParseDuration(stream.Duration),
            BitRate: ParseInt64(stream.BitRate),
            Language: language);
    }

    private static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds >= 0
            && !double.IsNaN(seconds)
            && !double.IsInfinity(seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    private static long? ParseInt64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : null;
    }

    private static int? ParseInt32(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : null;
    }

    private static double? ParseFrameRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "0/0" or "N/A")
        {
            return null;
        }

        var parts = value.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den)
            && den != 0)
        {
            return num / den;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps))
        {
            return fps;
        }

        return null;
    }
}
