using Clipify.Domain.Jobs;
using Clipify.Domain.Media;

namespace Clipify.Application.Media;

public interface IFFmpegLocator
{
    /// <summary>
    /// Lazily resolves the ffmpeg binary. Non-media jobs must not force resolution.
    /// </summary>
    string ResolveFFmpegPath();

    /// <summary>
    /// Lazily resolves the ffprobe binary.
    /// </summary>
    string ResolveFFprobePath();
}

public interface IFFmpegVersionProbe
{
    ValueTask<FFmpegToolVersion> ProbeFFmpegAsync(CancellationToken cancellationToken = default);

    ValueTask<FFmpegToolVersion> ProbeFFprobeAsync(CancellationToken cancellationToken = default);
}

public sealed record FFmpegToolVersion(
    string ExecutablePath,
    string VersionLine,
    string FullOutput);

public interface IFFmpegProgressParser
{
    /// <summary>
    /// Feeds an incremental stdout chunk (may contain partial lines).
    /// </summary>
    void Append(string chunk);

    /// <summary>
    /// Returns true when a complete progress snapshot is available (progress=continue|end).
    /// </summary>
    bool TryDequeueSnapshot(out FFmpegProgressSnapshot snapshot);
}

public sealed record FFmpegProgressSnapshot(
    TimeSpan? OutTime,
    double? Speed,
    double? Fps,
    long? Frame,
    long? TotalSize,
    bool IsEnd);

public interface IFFmpegProcessRunner
{
    Task<FFmpegProcessResult> RunAsync(
        FFmpegProcessSpec spec,
        CancellationToken cancellationToken = default);
}

public sealed record FFmpegProcessSpec(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    Action<FFmpegProgressSnapshot>? OnProgress = null,
    TimeSpan? CancelGracePeriod = null,
    int StderrLogLimitChars = 16_384);

public sealed record FFmpegProcessResult(
    int ExitCode,
    bool WasCanceled,
    string StderrSummary,
    FFmpegProgressSnapshot? LastProgress);

public interface IFFprobeClient
{
    Task<MediaInfo> ProbeAsync(string inputPath, CancellationToken cancellationToken = default);
}

public interface IFFmpegCommandBuilder<in TDefinition>
    where TDefinition : MediaJobDefinition
{
    /// <summary>
    /// Builds ArgumentList entries only — never a shell-rendered command string.
    /// </summary>
    IReadOnlyList<string> Build(TDefinition definition, string temporaryOutputPath);
}

public interface IOutputCommitter
{
    OutputPreparation Prepare(OutputCommitRequest request);

    ValueTask<OutputCommitResult> CommitAsync(
        OutputPreparation preparation,
        CancellationToken cancellationToken = default);

    void Cleanup(OutputPreparation preparation);
}

public sealed record OutputCommitRequest(
    MediaJobId JobId,
    string FinalOutputPath,
    OutputConflictPolicy ConflictPolicy);

public sealed class OutputPreparation
{
    public required MediaJobId JobId { get; init; }
    public required string FinalOutputPath { get; init; }
    public required string TemporaryOutputPath { get; init; }
    public required OutputConflictPolicy ConflictPolicy { get; init; }
    public bool SkipExecution { get; init; }
    public string? ExistingOutputPath { get; init; }
}

public sealed record OutputCommitResult(
    string CommittedPath,
    bool Skipped,
    long? SizeBytes);
