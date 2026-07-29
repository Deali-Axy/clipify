using System.ComponentModel;
using Clipify.Application.Abstractions;
using Clipify.Application.Jobs;
using Clipify.Application.Media;
using Clipify.Application.Parsing;
using Clipify.Domain.Jobs;
using Clipify.Domain.Media;
using Clipify.Mcp.Dto;
using Clipify.Mcp.Jobs;
using Clipify.Mcp.Security;
using ModelContextProtocol.Server;

namespace Clipify.Mcp.Tools;

[McpServerToolType]
public sealed class ClipifyMcpTools
{
    private readonly IMediaJobService _jobs;
    private readonly IProbeMediaUseCase _probe;
    private readonly AllowedRootPolicy _roots;
    private readonly TimeSpan _pollInterval;

    public ClipifyMcpTools(
        IMediaJobService jobs,
        IProbeMediaUseCase probe,
        AllowedRootPolicy roots,
        McpRuntimeOptions runtime)
    {
        _jobs = jobs;
        _probe = probe;
        _roots = roots;
        _pollInterval = runtime.PollInterval ?? TimeSpan.FromMilliseconds(500);
    }

    [McpServerTool(Name = "probe_media", Title = "Probe Media", ReadOnly = true, OpenWorld = false),
     Description("Probe a local media file with ffprobe. Synchronous read-only.")]
    public async Task<string> ProbeMedia(
        [Description("Absolute or relative path to an input media file under an allow-root.")] string input,
        CancellationToken cancellationToken)
    {
        if (!_roots.TryResolve(input, PathAccessKind.InputFile, out var resolved, out var pathError))
        {
            return Fail(pathError!);
        }

        try
        {
            var result = await _probe.ExecuteAsync(resolved!.CanonicalPath, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return Fail(result.Error!);
            }

            return Ok(McpDtoMapper.OkMedia(result.Value!));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FailException(ex);
        }
    }

    [McpServerTool(
         Name = "trim_video",
         Title = "Trim Video",
         ReadOnly = false,
         Destructive = true,
         Idempotent = false,
         OpenWorld = false),
     Description("Enqueue a video trim job. Returns job_id immediately; use wait_job to observe completion.")]
    public async Task<string> TrimVideo(
        [Description("Input media path under an allow-root.")] string input,
        [Description("Output media path under an allow-root. Parent directory must already exist.")] string output,
        [Description("Trim start time: integer milliseconds or HH:MM:SS[.fff].")] string start,
        [Description("Trim end time: integer milliseconds or HH:MM:SS[.fff].")] string end,
        [Description("Output conflict policy: fail (default), overwrite, rename, or skip.")] string? conflict_policy = null,
        CancellationToken cancellationToken = default)
    {
        if (!BoundaryParsers.TryParseTime(start, out var startTs, out var timeError)
            || !BoundaryParsers.TryParseTime(end, out var endTs, out timeError))
        {
            return Fail(timeError!);
        }

        TimeRange range;
        try
        {
            range = new TimeRange(startTs, endTs);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Fail(ClipifyError.Validation(ex.Message));
        }

        if (!BoundaryParsers.TryParseConflictPolicy(conflict_policy, out var policy, out var policyError))
        {
            return Fail(policyError!);
        }

        if (!_roots.TryResolve(input, PathAccessKind.InputFile, out var inPath, out var inError))
        {
            return Fail(inError!);
        }

        if (!_roots.TryResolve(output, PathAccessKind.OutputFile, out var outPath, out var outError))
        {
            return Fail(outError!);
        }

        var definition = new TrimMediaJobDefinition
        {
            InputPath = inPath!.CanonicalPath,
            OutputPath = outPath!.CanonicalPath,
            Range = range,
            ConflictPolicy = policy,
        };

        return await EnqueueAsync(definition, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(
         Name = "extract_audio",
         Title = "Extract Audio",
         ReadOnly = false,
         Destructive = true,
         Idempotent = false,
         OpenWorld = false),
     Description("Enqueue an audio extraction job. Returns job_id immediately.")]
    public async Task<string> ExtractAudio(
        [Description("Input media path under an allow-root.")] string input,
        [Description("Output audio path under an allow-root. Parent directory must already exist.")] string output,
        [Description("Audio format: copy, mp3 (default), aac, or wav.")] string? format = null,
        [Description("Output conflict policy: fail (default), overwrite, rename, or skip.")] string? conflict_policy = null,
        CancellationToken cancellationToken = default)
    {
        if (!BoundaryParsers.TryParseAudioFormat(format, out var audioFormat, out var formatError))
        {
            return Fail(formatError!);
        }

        if (!BoundaryParsers.TryParseConflictPolicy(conflict_policy, out var policy, out var policyError))
        {
            return Fail(policyError!);
        }

        if (!_roots.TryResolve(input, PathAccessKind.InputFile, out var inPath, out var inError))
        {
            return Fail(inError!);
        }

        if (!_roots.TryResolve(output, PathAccessKind.OutputFile, out var outPath, out var outError))
        {
            return Fail(outError!);
        }

        var definition = new ExtractAudioJobDefinition
        {
            InputPath = inPath!.CanonicalPath,
            OutputPath = outPath!.CanonicalPath,
            Format = audioFormat,
            ConflictPolicy = policy,
        };

        return await EnqueueAsync(definition, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(
         Name = "generate_thumbnail",
         Title = "Generate Thumbnail",
         ReadOnly = false,
         Destructive = true,
         Idempotent = false,
         OpenWorld = false),
     Description("Enqueue a thumbnail generation job. Returns job_id immediately.")]
    public async Task<string> GenerateThumbnail(
        [Description("Input media path under an allow-root.")] string input,
        [Description("Output image path under an allow-root. Parent directory must already exist.")] string output,
        [Description("Capture time: integer milliseconds or HH:MM:SS[.fff]. Defaults to 0.")] string? at = null,
        [Description("Image format: jpg (default) or png. Inferred from .png extension when omitted.")] string? format = null,
        [Description("Output conflict policy: fail (default), overwrite, rename, or skip.")] string? conflict_policy = null,
        CancellationToken cancellationToken = default)
    {
        var atTs = TimeSpan.Zero;
        if (!string.IsNullOrWhiteSpace(at))
        {
            if (!BoundaryParsers.TryParseTime(at, out atTs, out var atError))
            {
                return Fail(atError!);
            }
        }

        if (!BoundaryParsers.TryParseThumbnailFormat(format, out var imageFormat, out var formatError))
        {
            return Fail(formatError!);
        }

        if (string.IsNullOrWhiteSpace(format)
            && Path.GetExtension(output).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            imageFormat = ThumbnailImageFormat.Png;
        }

        if (!BoundaryParsers.TryParseConflictPolicy(conflict_policy, out var policy, out var policyError))
        {
            return Fail(policyError!);
        }

        if (!_roots.TryResolve(input, PathAccessKind.InputFile, out var inPath, out var inError))
        {
            return Fail(inError!);
        }

        if (!_roots.TryResolve(output, PathAccessKind.OutputFile, out var outPath, out var outError))
        {
            return Fail(outError!);
        }

        var definition = new ThumbnailJobDefinition
        {
            InputPath = inPath!.CanonicalPath,
            OutputPath = outPath!.CanonicalPath,
            At = atTs,
            Format = imageFormat,
            ConflictPolicy = policy,
        };

        return await EnqueueAsync(definition, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "list_jobs", Title = "List Jobs", ReadOnly = true, OpenWorld = false),
     Description("List media jobs with optional state filter and pagination (default take 20, max 100).")]
    public async Task<string> ListJobs(
        [Description("Optional comma-separated states: queued,running,succeeded,failed,canceling,canceled,interrupted.")]
        string? states = null,
        [Description("Number of jobs to skip. Default 0.")] int skip = 0,
        [Description("Page size. Default 20, maximum 100.")] int take = McpResponseLimits.DefaultListTake,
        CancellationToken cancellationToken = default)
    {
        if (!BoundaryParsers.TryParseJobStates(states, out var parsedStates, out var stateError))
        {
            return Fail(stateError!);
        }

        if (skip < 0)
        {
            return Fail(ClipifyError.Validation("skip must be >= 0."));
        }

        take = Math.Clamp(take <= 0 ? McpResponseLimits.DefaultListTake : take, 1, McpResponseLimits.MaxListTake);

        try
        {
            var snapshots = await _jobs.ListAsync(new MediaJobListQuery(parsedStates, skip, take), cancellationToken)
                .ConfigureAwait(false);
            return Ok(McpDtoMapper.OkJobs(snapshots));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FailException(ex);
        }
    }

    [McpServerTool(Name = "get_job", Title = "Get Job", ReadOnly = true, OpenWorld = false),
     Description("Get one job snapshot including limited artifacts and error summary.")]
    public async Task<string> GetJob(
        [Description("Job id (GUID).")] string job_id,
        CancellationToken cancellationToken)
    {
        if (!BoundaryParsers.TryParseJobId(job_id, out var id, out var error))
        {
            return Fail(error!);
        }

        try
        {
            var snapshot = await _jobs.GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return Fail(ClipifyError.NotFound($"Job not found: {id.Value}"));
            }

            return Ok(McpDtoMapper.OkJob(snapshot));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FailException(ex);
        }
    }

    [McpServerTool(Name = "wait_job", Title = "Wait Job", ReadOnly = true, OpenWorld = false),
     Description("Wait up to timeout_seconds (default 15, max 60) for a job terminal state. Timeout returns the latest snapshot and is not a job failure.")]
    public async Task<string> WaitJob(
        [Description("Job id (GUID).")] string job_id,
        [Description("Maximum seconds to wait. Default 15, maximum 60.")] int timeout_seconds = McpResponseLimits.DefaultWaitSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!BoundaryParsers.TryParseJobId(job_id, out var id, out var error))
        {
            return Fail(error!);
        }

        var seconds = timeout_seconds <= 0
            ? McpResponseLimits.DefaultWaitSeconds
            : Math.Min(timeout_seconds, McpResponseLimits.MaxWaitSeconds);

        try
        {
            var (snapshot, timedOut) = await McpJobWaiter.WaitAsync(
                    _jobs,
                    id,
                    TimeSpan.FromSeconds(seconds),
                    cancellationToken,
                    _pollInterval)
                .ConfigureAwait(false);

            var response = McpDtoMapper.OkJob(snapshot);
            response = response with
            {
                Result = new
                {
                    job = McpDtoMapper.FromSnapshot(snapshot),
                    wait = new McpWaitMetaDto(timedOut, seconds),
                },
            };
            return Ok(response);
        }
        catch (OperationCanceledException)
        {
            // Tool call cancelled by the client — do not cancel the queued job.
            throw;
        }
        catch (ClipifyException ex)
        {
            return Fail(ex.ToError());
        }
        catch (Exception ex)
        {
            return FailException(ex);
        }
    }

    [McpServerTool(
         Name = "cancel_job",
         Title = "Cancel Job",
         ReadOnly = false,
         Destructive = true,
         Idempotent = false,
         OpenWorld = false),
     Description("Request cancellation of a queued or running job. Does not wait for terminal state.")]
    public async Task<string> CancelJob(
        [Description("Job id (GUID).")] string job_id,
        CancellationToken cancellationToken)
    {
        if (!BoundaryParsers.TryParseJobId(job_id, out var id, out var error))
        {
            return Fail(error!);
        }

        try
        {
            await _jobs.RequestCancelAsync(id, cancellationToken).ConfigureAwait(false);
            var snapshot = await _jobs.GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return Fail(ClipifyError.NotFound($"Job not found: {id.Value}"));
            }

            return Ok(McpDtoMapper.OkJob(snapshot));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ClipifyError.NotFound(ex.Message));
        }
        catch (Exception ex)
        {
            return FailException(ex);
        }
    }

    [McpServerTool(
         Name = "retry_job",
         Title = "Retry Job",
         ReadOnly = false,
         Destructive = false,
         Idempotent = false,
         OpenWorld = false),
     Description("Create a new job from a failed, interrupted, or canceled job. Returns the new job_id immediately.")]
    public async Task<string> RetryJob(
        [Description("Source job id (GUID).")] string job_id,
        CancellationToken cancellationToken)
    {
        if (!BoundaryParsers.TryParseJobId(job_id, out var id, out var error))
        {
            return Fail(error!);
        }

        try
        {
            var newId = await _jobs.RetryAsync(id, cancellationToken).ConfigureAwait(false);
            var snapshot = await _jobs.GetAsync(newId, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return Ok(McpDtoMapper.OkJobId(newId, MediaJobState.Queued));
            }

            return Ok(McpDtoMapper.OkJob(snapshot));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ClipifyError.InvalidState(ex.Message));
        }
        catch (Exception ex)
        {
            return FailException(ex);
        }
    }

    private async Task<string> EnqueueAsync(MediaJobDefinition definition, CancellationToken cancellationToken)
    {
        try
        {
            var jobId = await _jobs.EnqueueAsync(definition, cancellationToken).ConfigureAwait(false);
            var snapshot = await _jobs.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return Ok(McpDtoMapper.OkJobId(jobId, MediaJobState.Queued));
            }

            return Ok(McpDtoMapper.OkJob(snapshot));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FailException(ex);
        }
    }

    private static string Ok(McpToolResponse response) => McpJson.Serialize(response);

    private static string Fail(ClipifyError error) => McpJson.Serialize(McpDtoMapper.Fail(error));

    private static string FailException(Exception ex) => McpJson.Serialize(McpDtoMapper.FailException(ex));
}
