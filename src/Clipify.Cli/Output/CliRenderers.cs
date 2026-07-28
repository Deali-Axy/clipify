using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clipify.Cli.Output;

public interface ICliRenderer
{
    OutputMode Mode { get; }

    void WriteProgress(CliProgressDto progress, string? jobId = null);

    void WriteResult(CliCommandResultDto result);

    void WriteDiagnostic(string message);

    void WriteWarning(string message);

    void WriteError(string message);
}

public abstract class CliRendererBase : ICliRenderer
{
    protected TextWriter Out { get; }
    protected TextWriter Error { get; }

    protected CliRendererBase(TextWriter output, TextWriter error)
    {
        Out = output;
        Error = error;
    }

    public abstract OutputMode Mode { get; }

    public abstract void WriteProgress(CliProgressDto progress, string? jobId = null);

    public abstract void WriteResult(CliCommandResultDto result);

    public virtual void WriteDiagnostic(string message) => Error.WriteLine(message);

    public virtual void WriteWarning(string message) => Error.WriteLine($"warning: {message}");

    public virtual void WriteError(string message) => Error.WriteLine($"error: {message}");
}

public sealed class TextCliRenderer : CliRendererBase
{
    public TextCliRenderer(TextWriter output, TextWriter error) : base(output, error)
    {
    }

    public override OutputMode Mode => OutputMode.Text;

    public override void WriteProgress(CliProgressDto progress, string? jobId = null)
    {
        var percent = progress.Fraction is null ? "?" : $"{progress.Fraction.Value * 100:0.#}%";
        var line = $"[{progress.Stage}] {percent}";
        if (!string.IsNullOrWhiteSpace(progress.Message))
        {
            line += $" {progress.Message}";
        }

        if (jobId is not null)
        {
            line = $"{jobId[..Math.Min(8, jobId.Length)]}… {line}";
        }

        // Progress goes to stderr so stdout stays a clean result channel when piped.
        Error.WriteLine(line);
    }

    public override void WriteResult(CliCommandResultDto result)
    {
        if (result.Doctor is not null)
        {
            WriteDoctor(result.Doctor);
            return;
        }

        if (result.Media is not null)
        {
            WriteMedia(result.Media);
            return;
        }

        if (result.Jobs is not null)
        {
            foreach (var job in result.Jobs)
            {
                WriteJob(job);
            }

            return;
        }

        if (result.Job is not null)
        {
            WriteJob(result.Job);
            return;
        }

        if (result.Error is not null)
        {
            WriteError($"{result.Error.Code}: {result.Error.Message}");
            return;
        }

        Out.WriteLine(result.Ok ? "ok" : "failed");
    }

    private void WriteDoctor(CliDoctorResultDto doctor)
    {
        Out.WriteLine(doctor.Ok ? "doctor: ok" : "doctor: failed");
        Out.WriteLine($"os: {doctor.Platform.Os} ({doctor.Platform.Architecture})");
        Out.WriteLine($"framework: {doctor.Platform.Framework}");
        Out.WriteLine($"data: {doctor.Paths.Root}");
        Out.WriteLine($"database: {doctor.Paths.DatabasePath}");
        foreach (var check in doctor.Checks)
        {
            var mark = check.Ok ? "ok" : "FAIL";
            Out.WriteLine($"[{mark}] {check.Name}: {check.Message}");
        }
    }

    private void WriteMedia(CliMediaInfoDto media)
    {
        Out.WriteLine($"path: {media.Path}");
        if (media.DurationMs is not null)
        {
            Out.WriteLine($"duration_ms: {media.DurationMs}");
        }

        if (media.FormatName is not null)
        {
            Out.WriteLine($"format: {media.FormatName}");
        }

        if (media.SizeBytes is not null)
        {
            Out.WriteLine($"size_bytes: {media.SizeBytes}");
        }

        foreach (var stream in media.Streams)
        {
            Out.WriteLine(
                $"stream[{stream.Index}]: {stream.CodecType} {stream.CodecName}" +
                (stream.Width is not null ? $" {stream.Width}x{stream.Height}" : string.Empty) +
                (stream.SampleRate is not null ? $" {stream.SampleRate}Hz" : string.Empty));
        }
    }

    private void WriteJob(CliJobDto job)
    {
        Out.WriteLine($"job_id: {job.JobId}");
        Out.WriteLine($"state: {job.State}");
        Out.WriteLine($"operation: {job.Operation}");
        if (job.Error is not null)
        {
            Out.WriteLine($"error: {job.Error.Code}: {job.Error.Message}");
        }

        foreach (var artifact in job.Artifacts)
        {
            Out.WriteLine($"artifact: {artifact.Path}" + (artifact.SizeBytes is null ? string.Empty : $" ({artifact.SizeBytes} bytes)"));
        }
    }
}

public sealed class JsonCliRenderer : CliRendererBase
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public JsonCliRenderer(TextWriter output, TextWriter error) : base(output, error)
    {
    }

    public override OutputMode Mode => OutputMode.Json;

    public override void WriteProgress(CliProgressDto progress, string? jobId = null)
    {
        // JSON mode: no progress on stdout.
    }

    public override void WriteResult(CliCommandResultDto result)
    {
        Out.WriteLine(JsonSerializer.Serialize(result, Options));
    }

    public override void WriteDiagnostic(string message) => Error.WriteLine(message);

    private static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}

public sealed class JsonlCliRenderer : CliRendererBase
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public JsonlCliRenderer(TextWriter output, TextWriter error) : base(output, error)
    {
    }

    public override OutputMode Mode => OutputMode.Jsonl;

    public override void WriteProgress(CliProgressDto progress, string? jobId = null)
    {
        WriteEvent(new CliJsonlEventDto(
            Event: "progress",
            At: DateTimeOffset.UtcNow.ToString("O"),
            JobId: jobId,
            Progress: progress));
    }

    public override void WriteResult(CliCommandResultDto result)
    {
        WriteEvent(new CliJsonlEventDto(
            Event: "result",
            At: DateTimeOffset.UtcNow.ToString("O"),
            JobId: result.Job?.JobId,
            Result: result,
            Error: result.Error));
    }

    private void WriteEvent(CliJsonlEventDto evt) =>
        Out.WriteLine(JsonSerializer.Serialize(evt, Options));
}

public static class CliRendererFactory
{
    public static ICliRenderer Create(OutputMode mode, TextWriter output, TextWriter error) => mode switch
    {
        OutputMode.Json => new JsonCliRenderer(output, error),
        OutputMode.Jsonl => new JsonlCliRenderer(output, error),
        _ => new TextCliRenderer(output, error),
    };
}
