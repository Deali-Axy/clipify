using System.Text.Json.Serialization;
using Clipify.Hosting;

namespace Clipify.Cli.Output;

/// <summary>
/// CLI-facing DTOs with stable snake_case JSON. Never serialize EF entities.
/// </summary>
public sealed record CliErrorDto(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("detail")] string? Detail = null);

public sealed record CliProgressDto(
    [property: JsonPropertyName("fraction")] double? Fraction,
    [property: JsonPropertyName("processed_ms")] long? ProcessedMs,
    [property: JsonPropertyName("total_ms")] long? TotalMs,
    [property: JsonPropertyName("speed")] double? Speed,
    [property: JsonPropertyName("eta_ms")] long? EtaMs,
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("updated_at")] string UpdatedAt);

public sealed record CliArtifactDto(
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("size_bytes")] long? SizeBytes,
    [property: JsonPropertyName("content_type")] string? ContentType,
    [property: JsonPropertyName("created_at")] string CreatedAt);

public sealed record CliJobDto(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("updated_at")] string UpdatedAt,
    [property: JsonPropertyName("started_at")] string? StartedAt,
    [property: JsonPropertyName("completed_at")] string? CompletedAt,
    [property: JsonPropertyName("retry_of_job_id")] string? RetryOfJobId,
    [property: JsonPropertyName("progress")] CliProgressDto? Progress,
    [property: JsonPropertyName("error")] CliErrorDto? Error,
    [property: JsonPropertyName("artifacts")] IReadOnlyList<CliArtifactDto> Artifacts);

public sealed record CliStreamDto(
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

public sealed record CliMediaInfoDto(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("duration_ms")] long? DurationMs,
    [property: JsonPropertyName("size_bytes")] long? SizeBytes,
    [property: JsonPropertyName("format_name")] string? FormatName,
    [property: JsonPropertyName("format_long_name")] string? FormatLongName,
    [property: JsonPropertyName("bit_rate")] long? BitRate,
    [property: JsonPropertyName("streams")] IReadOnlyList<CliStreamDto> Streams);

public sealed record CliDoctorCheckDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("detail")] string? Detail);

public sealed record CliDoctorPathsDto(
    [property: JsonPropertyName("root")] string Root,
    [property: JsonPropertyName("database_path")] string DatabasePath,
    [property: JsonPropertyName("lock_directory")] string LockDirectory,
    [property: JsonPropertyName("log_directory")] string LogDirectory);

public sealed record CliDoctorPlatformDto(
    [property: JsonPropertyName("os")] string Os,
    [property: JsonPropertyName("architecture")] string Architecture,
    [property: JsonPropertyName("framework")] string Framework,
    [property: JsonPropertyName("process_architecture")] string ProcessArchitecture,
    [property: JsonPropertyName("runtime_identifier")] string RuntimeIdentifier);

public sealed record CliDoctorResultDto(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("paths")] CliDoctorPathsDto Paths,
    [property: JsonPropertyName("platform")] CliDoctorPlatformDto Platform,
    [property: JsonPropertyName("checks")] IReadOnlyList<CliDoctorCheckDto> Checks);

public sealed record CliCommandResultDto(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("job")] CliJobDto? Job = null,
    [property: JsonPropertyName("media")] CliMediaInfoDto? Media = null,
    [property: JsonPropertyName("doctor")] CliDoctorResultDto? Doctor = null,
    [property: JsonPropertyName("jobs")] IReadOnlyList<CliJobDto>? Jobs = null,
    [property: JsonPropertyName("error")] CliErrorDto? Error = null);

public sealed record CliJsonlEventDto(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("at")] string At,
    [property: JsonPropertyName("job_id")] string? JobId = null,
    [property: JsonPropertyName("progress")] CliProgressDto? Progress = null,
    [property: JsonPropertyName("result")] CliCommandResultDto? Result = null,
    [property: JsonPropertyName("error")] CliErrorDto? Error = null);

public static class CliDtoMapper
{
    public static CliErrorDto FromError(Application.Abstractions.ClipifyError error) =>
        new(error.Code.ToString(), error.Message, error.Detail);

    public static CliProgressDto? FromProgress(Domain.Jobs.MediaJobProgress? progress)
    {
        if (progress is null)
        {
            return null;
        }

        return new CliProgressDto(
            Fraction: progress.Fraction,
            ProcessedMs: ToMs(progress.ProcessedDuration),
            TotalMs: ToMs(progress.TotalDuration),
            Speed: progress.Speed,
            EtaMs: ToMs(progress.EstimatedRemaining),
            Stage: progress.Stage,
            Message: progress.Message,
            UpdatedAt: progress.UpdatedAt.ToString("O"));
    }

    public static CliArtifactDto FromArtifact(Domain.Jobs.MediaArtifact artifact) =>
        new(
            ArtifactId: artifact.ArtifactId,
            Kind: artifact.Kind,
            Path: artifact.Path,
            SizeBytes: artifact.SizeBytes,
            ContentType: artifact.ContentType,
            CreatedAt: artifact.CreatedAt.ToString("O"));

    public static CliJobDto FromSnapshot(Domain.Jobs.MediaJobSnapshot snapshot)
    {
        CliErrorDto? error = null;
        if (!string.IsNullOrWhiteSpace(snapshot.ErrorCode) || !string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
        {
            error = new CliErrorDto(
                snapshot.ErrorCode ?? "Unknown",
                snapshot.ErrorMessage ?? "Job failed.",
                null);
        }

        return new CliJobDto(
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
            Artifacts: snapshot.Artifacts.Select(FromArtifact).ToArray());
    }

    public static CliMediaInfoDto FromMedia(Domain.Media.MediaInfo info) =>
        new(
            Path: info.Path,
            DurationMs: ToMs(info.Duration),
            SizeBytes: info.SizeBytes,
            FormatName: info.FormatName,
            FormatLongName: info.FormatLongName,
            BitRate: info.BitRate,
            Streams: info.Streams.Select(s => new CliStreamDto(
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
                s.Language)).ToArray());

    public static CliDoctorResultDto FromDoctor(ClipifyDoctorReport report) =>
        new(
            Ok: report.Ok,
            Paths: new CliDoctorPathsDto(
                report.Paths.Root,
                report.Paths.DatabasePath,
                report.Paths.LockDirectory,
                report.Paths.LogDirectory),
            Platform: new CliDoctorPlatformDto(
                report.Platform.Os,
                report.Platform.Architecture,
                report.Platform.FrameworkDescription,
                report.Platform.ProcessArchitecture,
                report.Platform.RuntimeIdentifier),
            Checks: report.Checks.Select(c => new CliDoctorCheckDto(c.Name, c.Ok, c.Message, c.Detail)).ToArray());

    private static long? ToMs(TimeSpan? value) =>
        value is null ? null : (long)value.Value.TotalMilliseconds;

    private static string ToSnake(string pascal)
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
