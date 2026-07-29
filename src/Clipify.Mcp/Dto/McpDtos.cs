using System.Text.Json;
using System.Text.Json.Serialization;
using Clipify.Application.Abstractions;
using Clipify.Domain.Jobs;
using Clipify.Domain.Media;

namespace Clipify.Mcp.Dto;

public static class McpResponseLimits
{
    public const int DefaultListTake = 20;
    public const int MaxListTake = 100;
    public const int MaxArtifacts = 20;
    public const int MaxStreams = 32;
    public const int MaxErrorMessageChars = 512;
    public const int MaxErrorDetailChars = 256;
    public const int MaxProgressMessageChars = 256;
    public const int MaxWarnings = 8;
    public const int DefaultWaitSeconds = 15;
    public const int MaxWaitSeconds = 60;
}

public sealed record McpErrorDto(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("detail")] string? Detail = null);

public sealed record McpProgressDto(
    [property: JsonPropertyName("fraction")] double? Fraction,
    [property: JsonPropertyName("processed_ms")] long? ProcessedMs,
    [property: JsonPropertyName("total_ms")] long? TotalMs,
    [property: JsonPropertyName("speed")] double? Speed,
    [property: JsonPropertyName("eta_ms")] long? EtaMs,
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("updated_at")] string UpdatedAt);

public sealed record McpArtifactDto(
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("size_bytes")] long? SizeBytes,
    [property: JsonPropertyName("content_type")] string? ContentType,
    [property: JsonPropertyName("created_at")] string CreatedAt);

public sealed record McpJobDto(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("updated_at")] string UpdatedAt,
    [property: JsonPropertyName("started_at")] string? StartedAt,
    [property: JsonPropertyName("completed_at")] string? CompletedAt,
    [property: JsonPropertyName("retry_of_job_id")] string? RetryOfJobId,
    [property: JsonPropertyName("progress")] McpProgressDto? Progress,
    [property: JsonPropertyName("error")] McpErrorDto? Error,
    [property: JsonPropertyName("artifacts")] IReadOnlyList<McpArtifactDto> Artifacts);

public sealed record McpStreamDto(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("codec_type")] string CodecType,
    [property: JsonPropertyName("codec_name")] string? CodecName,
    [property: JsonPropertyName("width")] int? Width,
    [property: JsonPropertyName("height")] int? Height,
    [property: JsonPropertyName("frame_rate")] double? FrameRate,
    [property: JsonPropertyName("sample_rate")] int? SampleRate,
    [property: JsonPropertyName("channels")] int? Channels,
    [property: JsonPropertyName("duration_ms")] long? DurationMs,
    [property: JsonPropertyName("bit_rate")] long? BitRate,
    [property: JsonPropertyName("language")] string? Language);

public sealed record McpMediaInfoDto(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("duration_ms")] long? DurationMs,
    [property: JsonPropertyName("size_bytes")] long? SizeBytes,
    [property: JsonPropertyName("format_name")] string? FormatName,
    [property: JsonPropertyName("format_long_name")] string? FormatLongName,
    [property: JsonPropertyName("bit_rate")] long? BitRate,
    [property: JsonPropertyName("streams")] IReadOnlyList<McpStreamDto> Streams);

public sealed record McpWaitMetaDto(
    [property: JsonPropertyName("timed_out")] bool TimedOut,
    [property: JsonPropertyName("waited_seconds")] int WaitedSeconds);

/// <summary>
/// Unified MCP tool response envelope. Never serialize Domain/EF entities directly.
/// </summary>
public sealed record McpToolResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("job_id")] string? JobId = null,
    [property: JsonPropertyName("state")] string? State = null,
    [property: JsonPropertyName("result")] object? Result = null,
    [property: JsonPropertyName("error")] McpErrorDto? Error = null,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string>? Warnings = null);

public static class McpJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string Serialize(McpToolResponse response) =>
        JsonSerializer.Serialize(response, Options);
}

public static class McpDtoMapper
{
    public static McpErrorDto FromError(ClipifyError error) =>
        new(
            error.Code.ToString(),
            Truncate(error.Message, McpResponseLimits.MaxErrorMessageChars) ?? error.Message,
            Truncate(error.Detail, McpResponseLimits.MaxErrorDetailChars));

    public static McpErrorDto FromException(Exception ex)
    {
        if (ex is ClipifyException clipify)
        {
            return FromError(clipify.ToError());
        }

        return new McpErrorDto(
            nameof(ClipifyErrorCode.Internal),
            Truncate(ex.Message, McpResponseLimits.MaxErrorMessageChars) ?? "Internal error.");
    }

    public static McpProgressDto? FromProgress(MediaJobProgress? progress)
    {
        if (progress is null)
        {
            return null;
        }

        return new McpProgressDto(
            Fraction: progress.Fraction,
            ProcessedMs: ToMs(progress.ProcessedDuration),
            TotalMs: ToMs(progress.TotalDuration),
            Speed: progress.Speed,
            EtaMs: ToMs(progress.EstimatedRemaining),
            Stage: progress.Stage,
            Message: Truncate(progress.Message, McpResponseLimits.MaxProgressMessageChars),
            UpdatedAt: progress.UpdatedAt.ToString("O"));
    }

    public static McpArtifactDto FromArtifact(MediaArtifact artifact) =>
        new(
            ArtifactId: artifact.ArtifactId,
            Kind: artifact.Kind,
            Path: artifact.Path,
            SizeBytes: artifact.SizeBytes,
            ContentType: artifact.ContentType,
            CreatedAt: artifact.CreatedAt.ToString("O"));

    public static McpJobDto FromSnapshot(MediaJobSnapshot snapshot)
    {
        McpErrorDto? error = null;
        if (!string.IsNullOrWhiteSpace(snapshot.ErrorCode) || !string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
        {
            error = new McpErrorDto(
                snapshot.ErrorCode ?? "Unknown",
                Truncate(snapshot.ErrorMessage ?? "Job failed.", McpResponseLimits.MaxErrorMessageChars)!,
                null);
        }

        var artifacts = snapshot.Artifacts
            .Take(McpResponseLimits.MaxArtifacts)
            .Select(FromArtifact)
            .ToArray();

        return new McpJobDto(
            JobId: snapshot.Id.Value,
            State: ToSnake(snapshot.State.ToString()),
            Operation: snapshot.DefinitionKind,
            CreatedAt: snapshot.CreatedAt.ToString("O"),
            UpdatedAt: snapshot.UpdatedAt.ToString("O"),
            StartedAt: snapshot.StartedAt?.ToString("O"),
            CompletedAt: snapshot.CompletedAt?.ToString("O"),
            RetryOfJobId: snapshot.RetryOfJobId?.Value,
            Progress: FromProgress(snapshot.Progress),
            Error: error,
            Artifacts: artifacts);
    }

    public static McpMediaInfoDto FromMedia(MediaInfo info) =>
        new(
            Path: info.Path,
            DurationMs: ToMs(info.Duration),
            SizeBytes: info.SizeBytes,
            FormatName: info.FormatName,
            FormatLongName: info.FormatLongName,
            BitRate: info.BitRate,
            Streams: info.Streams
                .Take(McpResponseLimits.MaxStreams)
                .Select(s => new McpStreamDto(
                    s.Index,
                    s.CodecType,
                    s.CodecName,
                    s.Width,
                    s.Height,
                    s.FrameRate,
                    s.SampleRate,
                    s.Channels,
                    ToMs(s.Duration),
                    s.BitRate,
                    s.Language))
                .ToArray());

    public static McpToolResponse OkJob(MediaJobSnapshot snapshot, IReadOnlyList<string>? warnings = null) =>
        new(
            Ok: true,
            JobId: snapshot.Id.Value,
            State: ToSnake(snapshot.State.ToString()),
            Result: FromSnapshot(snapshot),
            Warnings: CapWarnings(warnings));

    public static McpToolResponse OkJobId(MediaJobId jobId, MediaJobState state, IReadOnlyList<string>? warnings = null) =>
        new(
            Ok: true,
            JobId: jobId.Value,
            State: ToSnake(state.ToString()),
            Result: new { job_id = jobId.Value, state = ToSnake(state.ToString()) },
            Warnings: CapWarnings(warnings));

    public static McpToolResponse OkMedia(MediaInfo info) =>
        new(Ok: true, Result: FromMedia(info));

    public static McpToolResponse OkJobs(IReadOnlyList<MediaJobSnapshot> snapshots) =>
        new(
            Ok: true,
            Result: new { jobs = snapshots.Select(FromSnapshot).ToArray(), count = snapshots.Count });

    public static McpToolResponse Fail(ClipifyError error, string? jobId = null, string? state = null) =>
        new(Ok: false, JobId: jobId, State: state, Error: FromError(error));

    public static McpToolResponse FailException(Exception ex, string? jobId = null, string? state = null) =>
        new(Ok: false, JobId: jobId, State: state, Error: FromException(ex));

    private static IReadOnlyList<string>? CapWarnings(IReadOnlyList<string>? warnings)
    {
        if (warnings is null || warnings.Count == 0)
        {
            return null;
        }

        return warnings
            .Take(McpResponseLimits.MaxWarnings)
            .Select(w => Truncate(w, McpResponseLimits.MaxErrorMessageChars)!)
            .ToArray();
    }

    private static long? ToMs(TimeSpan? value) =>
        value is null ? null : (long)value.Value.TotalMilliseconds;

    private static string? Truncate(string? text, int max)
    {
        if (text is null)
        {
            return null;
        }

        return text.Length <= max ? text : text[..max];
    }

    internal static string ToSnake(string pascal)
    {
        if (string.IsNullOrEmpty(pascal))
        {
            return pascal;
        }

        Span<char> buffer = stackalloc char[pascal.Length * 2];
        var n = 0;
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (char.IsUpper(c) && i > 0)
            {
                buffer[n++] = '_';
            }

            buffer[n++] = char.ToLowerInvariant(c);
        }

        return new string(buffer[..n]);
    }
}
