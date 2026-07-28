using Clipify.Application.Abstractions;
using Clipify.Application.Jobs;
using Clipify.Application.Media;
using Clipify.Domain.Jobs;
using Clipify.Domain.Media;
using Microsoft.Extensions.Logging;

namespace Clipify.FFmpeg.Handlers;

public abstract class MediaJobHandlerBase
{
    private readonly IFFmpegLocator _locator;
    private readonly IFFmpegProcessRunner _runner;
    private readonly IFFprobeClient _ffprobe;
    private readonly IOutputCommitter _committer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    protected MediaJobHandlerBase(
        IFFmpegLocator locator,
        IFFmpegProcessRunner runner,
        IFFprobeClient ffprobe,
        IOutputCommitter committer,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _locator = locator;
        _runner = runner;
        _ffprobe = ffprobe;
        _committer = committer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected async Task ExecutePipelineAsync(
        string inputPath,
        string outputPath,
        OutputConflictPolicy conflictPolicy,
        TimeSpan? totalDurationHint,
        Func<string, IReadOnlyList<string>> buildArgs,
        string artifactKind,
        string? contentType,
        MediaJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        OutputPreparation? preparation = null;
        try
        {
            await ReportAsync(context, "validate", 0, null, totalDurationHint, cancellationToken)
                .ConfigureAwait(false);
            ValidatePaths(inputPath, outputPath);

            await ReportAsync(context, "probe", 0.05, null, totalDurationHint, cancellationToken)
                .ConfigureAwait(false);
            var media = await _ffprobe.ProbeAsync(Path.GetFullPath(inputPath), cancellationToken)
                .ConfigureAwait(false);
            var total = totalDurationHint ?? media.Duration;

            await ReportAsync(context, "prepare", 0.1, null, total, cancellationToken)
                .ConfigureAwait(false);
            preparation = _committer.Prepare(new OutputCommitRequest(
                context.Snapshot.Id,
                Path.GetFullPath(outputPath),
                conflictPolicy));

            if (preparation.SkipExecution)
            {
                var skipped = await _committer.CommitAsync(preparation, cancellationToken).ConfigureAwait(false);
                await CompleteArtifactAsync(context, artifactKind, skipped.CommittedPath, skipped.SizeBytes, contentType, cancellationToken)
                    .ConfigureAwait(false);
                await ReportAsync(context, "completed", 1, total, total, cancellationToken).ConfigureAwait(false);
                return;
            }

            await ReportAsync(context, "run", 0.15, null, total, cancellationToken).ConfigureAwait(false);
            var args = buildArgs(preparation.TemporaryOutputPath);
            var lastProgressPersist = DateTimeOffset.MinValue;
            var result = await _runner.RunAsync(
                    new FFmpegProcessSpec(
                        _locator.ResolveFFmpegPath(),
                        args,
                        OnProgress: snapshot =>
                        {
                            var now = _timeProvider.GetUtcNow();
                            if (now - lastProgressPersist < TimeSpan.FromMilliseconds(500) && !snapshot.IsEnd)
                            {
                                return;
                            }

                            lastProgressPersist = now;
                            ReportProgressSnapshotAsync(context, snapshot, total, now, cancellationToken)
                                .GetAwaiter()
                                .GetResult();
                        }),
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.WasCanceled || cancellationToken.IsCancellationRequested)
            {
                _committer.Cleanup(preparation);
                throw new OperationCanceledException(cancellationToken);
            }

            if (result.ExitCode != 0)
            {
                _logger.LogWarning(
                    "FFmpeg failed job {JobId} exit={ExitCode} stderr={Stderr}",
                    context.Snapshot.Id,
                    result.ExitCode,
                    result.StderrSummary);
                _committer.Cleanup(preparation);
                throw new ClipifyException(
                    ClipifyErrorCode.FfmpegFailed,
                    $"FFmpeg failed with exit code {result.ExitCode}.");
            }

            await ReportAsync(context, "verify", 0.9, total, total, cancellationToken).ConfigureAwait(false);
            var committed = await _committer.CommitAsync(preparation, cancellationToken).ConfigureAwait(false);

            await ReportAsync(context, "commit", 0.95, total, total, cancellationToken).ConfigureAwait(false);
            await CompleteArtifactAsync(
                    context,
                    artifactKind,
                    committed.CommittedPath,
                    committed.SizeBytes,
                    contentType,
                    cancellationToken)
                .ConfigureAwait(false);

            await ReportAsync(context, "completed", 1, total, total, cancellationToken).ConfigureAwait(false);
            preparation = null;
        }
        catch (OperationCanceledException)
        {
            if (preparation is not null)
            {
                _committer.Cleanup(preparation);
            }

            throw;
        }
        catch
        {
            if (preparation is not null)
            {
                _committer.Cleanup(preparation);
            }

            throw;
        }
    }

    private async Task ReportProgressSnapshotAsync(
        MediaJobExecutionContext context,
        FFmpegProgressSnapshot snapshot,
        TimeSpan? total,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        double? fraction = null;
        if (snapshot.OutTime is { } processed && total is { } t && t > TimeSpan.Zero)
        {
            fraction = Math.Clamp(processed.TotalMilliseconds / t.TotalMilliseconds, 0, 1);
        }

        try
        {
            await context.ReportProgressAsync(
                    MediaJobProgress.Create(
                        "run",
                        now,
                        fraction: fraction,
                        processedDuration: snapshot.OutTime,
                        totalDuration: total,
                        speed: snapshot.Speed),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Progress report failed for job {JobId}", context.Snapshot.Id);
        }
    }

    private async Task ReportAsync(
        MediaJobExecutionContext context,
        string stage,
        double? fraction,
        TimeSpan? processed,
        TimeSpan? total,
        CancellationToken cancellationToken)
    {
        await context.ReportProgressAsync(
                MediaJobProgress.Create(
                    stage,
                    _timeProvider.GetUtcNow(),
                    fraction: fraction,
                    processedDuration: processed,
                    totalDuration: total),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task CompleteArtifactAsync(
        MediaJobExecutionContext context,
        string kind,
        string path,
        long? sizeBytes,
        string? contentType,
        CancellationToken cancellationToken)
    {
        await context.AddArtifactAsync(
                MediaArtifact.Create(
                    context.Snapshot.Id,
                    kind,
                    path,
                    _timeProvider.GetUtcNow(),
                    sizeBytes: sizeBytes,
                    contentType: contentType),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void ValidatePaths(string inputPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ClipifyException(ClipifyErrorCode.Validation, "Input path is required.");
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ClipifyException(ClipifyErrorCode.Validation, "Output path is required.");
        }

        var fullInput = Path.GetFullPath(inputPath);
        if (!File.Exists(fullInput))
        {
            throw new ClipifyException(ClipifyErrorCode.NotFound, $"Input file not found: {fullInput}");
        }
    }
}

public sealed class TrimMediaJobHandler : MediaJobHandlerBase, IMediaJobHandler<TrimMediaJobDefinition>
{
    private readonly IFFmpegCommandBuilder<TrimMediaJobDefinition> _builder;

    public TrimMediaJobHandler(
        IFFmpegLocator locator,
        IFFmpegProcessRunner runner,
        IFFprobeClient ffprobe,
        IOutputCommitter committer,
        IFFmpegCommandBuilder<TrimMediaJobDefinition> builder,
        TimeProvider timeProvider,
        ILogger<TrimMediaJobHandler> logger)
        : base(locator, runner, ffprobe, committer, timeProvider, logger)
    {
        _builder = builder;
    }

    public Task ExecuteAsync(
        TrimMediaJobDefinition definition,
        MediaJobExecutionContext context,
        CancellationToken cancellationToken) =>
        ExecutePipelineAsync(
            definition.InputPath,
            definition.OutputPath,
            definition.ConflictPolicy,
            definition.Range.Duration,
            temp => _builder.Build(definition, temp),
            artifactKind: "trimmed_media",
            contentType: null,
            context,
            cancellationToken);
}

public sealed class ExtractAudioJobHandler : MediaJobHandlerBase, IMediaJobHandler<ExtractAudioJobDefinition>
{
    private readonly IFFmpegCommandBuilder<ExtractAudioJobDefinition> _builder;

    public ExtractAudioJobHandler(
        IFFmpegLocator locator,
        IFFmpegProcessRunner runner,
        IFFprobeClient ffprobe,
        IOutputCommitter committer,
        IFFmpegCommandBuilder<ExtractAudioJobDefinition> builder,
        TimeProvider timeProvider,
        ILogger<ExtractAudioJobHandler> logger)
        : base(locator, runner, ffprobe, committer, timeProvider, logger)
    {
        _builder = builder;
    }

    public Task ExecuteAsync(
        ExtractAudioJobDefinition definition,
        MediaJobExecutionContext context,
        CancellationToken cancellationToken) =>
        ExecutePipelineAsync(
            definition.InputPath,
            definition.OutputPath,
            definition.ConflictPolicy,
            totalDurationHint: null,
            temp => _builder.Build(definition, temp),
            artifactKind: "extracted_audio",
            contentType: ContentTypeFor(definition.Format),
            context,
            cancellationToken);

    private static string? ContentTypeFor(AudioOutputFormat format) => format switch
    {
        AudioOutputFormat.Mp3 => "audio/mpeg",
        AudioOutputFormat.Aac => "audio/aac",
        AudioOutputFormat.Wav => "audio/wav",
        _ => null,
    };
}

public sealed class ThumbnailJobHandler : MediaJobHandlerBase, IMediaJobHandler<ThumbnailJobDefinition>
{
    private readonly IFFmpegCommandBuilder<ThumbnailJobDefinition> _builder;

    public ThumbnailJobHandler(
        IFFmpegLocator locator,
        IFFmpegProcessRunner runner,
        IFFprobeClient ffprobe,
        IOutputCommitter committer,
        IFFmpegCommandBuilder<ThumbnailJobDefinition> builder,
        TimeProvider timeProvider,
        ILogger<ThumbnailJobHandler> logger)
        : base(locator, runner, ffprobe, committer, timeProvider, logger)
    {
        _builder = builder;
    }

    public Task ExecuteAsync(
        ThumbnailJobDefinition definition,
        MediaJobExecutionContext context,
        CancellationToken cancellationToken) =>
        ExecutePipelineAsync(
            definition.InputPath,
            definition.OutputPath,
            definition.ConflictPolicy,
            totalDurationHint: null,
            temp => _builder.Build(definition, temp),
            artifactKind: "thumbnail",
            contentType: definition.Format == ThumbnailImageFormat.Png ? "image/png" : "image/jpeg",
            context,
            cancellationToken);
}
