using Clipify.Application.Abstractions;
using Clipify.Application.Jobs;
using Clipify.Application.Media;
using Clipify.Cli.Output;
using Clipify.Cli.Parsing;
using Clipify.Domain.Jobs;
using Clipify.Domain.Media;
using Clipify.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Clipify.Cli.Commands;

internal static class CommandHandlers
{
    public static async Task<int> DoctorAsync(CliCommandContext ctx, CancellationToken ct)
    {
        ctx.EnsureDiagnosticsHost();
        var doctor = ctx.Services.GetRequiredService<IClipifyDoctor>();
        var report = await doctor.RunAsync(ct).ConfigureAwait(false);
        var dto = CliDtoMapper.FromDoctor(report);
        ctx.Renderer.WriteResult(new CliCommandResultDto(
            Ok: report.Ok,
            Command: "doctor",
            Doctor: dto,
            Error: report.Ok
                ? null
                : new CliErrorDto("ToolUnavailable", "One or more doctor checks failed.")));

        if (!report.Ok)
        {
            var ffmpegFailed = report.Checks.Any(c =>
                (c.Name is "ffmpeg" or "ffprobe") && !c.Ok);
            return ffmpegFailed ? CliExitCode.ToolUnavailable : CliExitCode.InternalError;
        }

        return CliExitCode.Success;
    }

    public static async Task<int> ProbeAsync(CliCommandContext ctx, string input, CancellationToken ct)
    {
        await ctx.EnsureHostStartedAsync(ct).ConfigureAwait(false);
        var useCase = ctx.Services.GetRequiredService<IProbeMediaUseCase>();
        var result = await useCase.ExecuteAsync(input, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            var error = result.Error!;
            ctx.Renderer.WriteResult(new CliCommandResultDto(
                Ok: false,
                Command: "probe",
                Error: CliDtoMapper.FromError(error)));
            return ExitCodeMapper.FromError(error);
        }

        ctx.Renderer.WriteResult(new CliCommandResultDto(
            Ok: true,
            Command: "probe",
            Media: CliDtoMapper.FromMedia(result.Value!)));
        return CliExitCode.Success;
    }

    public static async Task<int> TrimAsync(
        CliCommandContext ctx,
        string input,
        string startText,
        string endText,
        string output,
        string? conflictText,
        CancellationToken ct)
    {
        if (!CliParsers.TryParseTime(startText, out var start, out var timeError)
            || !CliParsers.TryParseTime(endText, out var end, out timeError))
        {
            return FailValidation(ctx, "trim", timeError!);
        }

        TimeRange range;
        try
        {
            range = new TimeRange(start, end);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return FailValidation(ctx, "trim", ClipifyError.Validation(ex.Message));
        }

        if (!CliParsers.TryParseConflictPolicy(conflictText, out var policy, out var policyError))
        {
            return FailValidation(ctx, "trim", policyError!);
        }

        var definition = new TrimMediaJobDefinition
        {
            InputPath = Path.GetFullPath(input),
            OutputPath = Path.GetFullPath(output),
            Range = range,
            ConflictPolicy = policy,
        };

        return await RunMediaJobAsync(ctx, "trim", definition, ct).ConfigureAwait(false);
    }

    public static async Task<int> ExtractAudioAsync(
        CliCommandContext ctx,
        string input,
        string output,
        string? formatText,
        string? conflictText,
        CancellationToken ct)
    {
        if (!CliParsers.TryParseAudioFormat(formatText, out var format, out var formatError))
        {
            return FailValidation(ctx, "extract-audio", formatError!);
        }

        if (!CliParsers.TryParseConflictPolicy(conflictText, out var policy, out var policyError))
        {
            return FailValidation(ctx, "extract-audio", policyError!);
        }

        var definition = new ExtractAudioJobDefinition
        {
            InputPath = Path.GetFullPath(input),
            OutputPath = Path.GetFullPath(output),
            Format = format,
            ConflictPolicy = policy,
        };

        return await RunMediaJobAsync(ctx, "extract-audio", definition, ct).ConfigureAwait(false);
    }

    public static async Task<int> ThumbnailAsync(
        CliCommandContext ctx,
        string input,
        string output,
        string? atText,
        string? formatText,
        string? conflictText,
        CancellationToken ct)
    {
        var at = TimeSpan.Zero;
        if (!string.IsNullOrWhiteSpace(atText))
        {
            if (!CliParsers.TryParseTime(atText, out at, out var atError))
            {
                return FailValidation(ctx, "thumbnail", atError!);
            }
        }

        if (!CliParsers.TryParseThumbnailFormat(formatText, out var format, out var formatError))
        {
            return FailValidation(ctx, "thumbnail", formatError!);
        }

        if (string.IsNullOrWhiteSpace(formatText))
        {
            var ext = Path.GetExtension(output);
            if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                format = ThumbnailImageFormat.Png;
            }
        }

        if (!CliParsers.TryParseConflictPolicy(conflictText, out var policy, out var policyError))
        {
            return FailValidation(ctx, "thumbnail", policyError!);
        }

        var definition = new ThumbnailJobDefinition
        {
            InputPath = Path.GetFullPath(input),
            OutputPath = Path.GetFullPath(output),
            At = at,
            Format = format,
            ConflictPolicy = policy,
        };

        return await RunMediaJobAsync(ctx, "thumbnail", definition, ct).ConfigureAwait(false);
    }

    public static async Task<int> JobsListAsync(CliCommandContext ctx, string? stateText, int take, CancellationToken ct)
    {
        await ctx.EnsureHostStartedAsync(ct).ConfigureAwait(false);

        IReadOnlyList<MediaJobState>? states = null;
        if (!string.IsNullOrWhiteSpace(stateText))
        {
            if (!Enum.TryParse<MediaJobState>(stateText.Trim(), ignoreCase: true, out var state)
                || !Enum.IsDefined(state))
            {
                return FailValidation(
                    ctx,
                    "jobs list",
                    ClipifyError.Validation(
                        "Invalid job state filter.",
                        stateText));
            }

            states = [state];
        }

        take = Math.Clamp(take <= 0 ? 20 : take, 1, 100);
        var snapshots = await ctx.Jobs.ListAsync(new MediaJobListQuery(states, 0, take), ct)
            .ConfigureAwait(false);

        ctx.Renderer.WriteResult(new CliCommandResultDto(
            Ok: true,
            Command: "jobs list",
            Jobs: snapshots.Select(CliDtoMapper.FromSnapshot).ToArray()));
        return CliExitCode.Success;
    }

    public static async Task<int> JobsGetAsync(CliCommandContext ctx, string jobIdText, CancellationToken ct)
    {
        if (!CliParsers.TryParseJobId(jobIdText, out var jobId, out var error))
        {
            return FailValidation(ctx, "jobs get", error!);
        }

        await ctx.EnsureHostStartedAsync(ct).ConfigureAwait(false);
        var snapshot = await ctx.Jobs.GetAsync(jobId, ct).ConfigureAwait(false);
        if (snapshot is null)
        {
            var notFound = ClipifyError.NotFound($"Job not found: {jobId.Value}");
            ctx.Renderer.WriteResult(new CliCommandResultDto(
                Ok: false,
                Command: "jobs get",
                Error: CliDtoMapper.FromError(notFound)));
            return ExitCodeMapper.FromError(notFound);
        }

        ctx.Renderer.WriteResult(new CliCommandResultDto(
            Ok: true,
            Command: "jobs get",
            Job: CliDtoMapper.FromSnapshot(snapshot)));
        return CliExitCode.Success;
    }

    public static async Task<int> JobsWaitAsync(CliCommandContext ctx, string jobIdText, CancellationToken ct)
    {
        if (!CliParsers.TryParseJobId(jobIdText, out var jobId, out var error))
        {
            return FailValidation(ctx, "jobs wait", error!);
        }

        await ctx.EnsureHostStartedAsync(ct).ConfigureAwait(false);
        ctx.AttachCancelKeyHandler();
        ctx.SetActiveJob(jobId);

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, ctx.ProcessToken);
            var snapshot = await JobWaiter.WaitForTerminalAsync(
                    ctx.Jobs,
                    jobId,
                    ctx.Renderer,
                    linked.Token,
                    ctx.Runtime.PollInterval)
                .ConfigureAwait(false);

            ctx.Renderer.WriteResult(new CliCommandResultDto(
                Ok: snapshot.State == MediaJobState.Succeeded,
                Command: "jobs wait",
                Job: CliDtoMapper.FromSnapshot(snapshot)));
            return ExitCodeMapper.FromJobSnapshot(snapshot);
        }
        catch (ClipifyException ex) when (ex.Code == ClipifyErrorCode.NotFound)
        {
            ctx.Renderer.WriteResult(new CliCommandResultDto(
                Ok: false,
                Command: "jobs wait",
                Error: CliDtoMapper.FromError(ex.ToError())));
            return ExitCodeMapper.FromError(ex.ToError());
        }
        catch (OperationCanceledException)
        {
            var snapshot = await ctx.Jobs.GetAsync(jobId, CancellationToken.None).ConfigureAwait(false);
            if (snapshot is not null)
            {
                ctx.Renderer.WriteResult(new CliCommandResultDto(
                    Ok: false,
                    Command: "jobs wait",
                    Job: CliDtoMapper.FromSnapshot(snapshot),
                    Error: new CliErrorDto("Canceled", "Wait canceled.")));
                return snapshot.IsTerminal
                    ? ExitCodeMapper.FromJobSnapshot(snapshot)
                    : CliExitCode.Canceled;
            }

            return CliExitCode.Canceled;
        }
        finally
        {
            ctx.ClearActiveJob();
        }
    }

    public static async Task<int> JobsCancelAsync(CliCommandContext ctx, string jobIdText, CancellationToken ct)
    {
        if (!CliParsers.TryParseJobId(jobIdText, out var jobId, out var error))
        {
            return FailValidation(ctx, "jobs cancel", error!);
        }

        await ctx.EnsureHostStartedAsync(ct).ConfigureAwait(false);
        try
        {
            await ctx.Jobs.RequestCancelAsync(jobId, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            var notFound = ClipifyError.NotFound(ex.Message);
            ctx.Renderer.WriteResult(new CliCommandResultDto(
                Ok: false,
                Command: "jobs cancel",
                Error: CliDtoMapper.FromError(notFound)));
            return ExitCodeMapper.FromError(notFound);
        }

        var snapshot = await ctx.Jobs.GetAsync(jobId, ct).ConfigureAwait(false);
        ctx.Renderer.WriteResult(new CliCommandResultDto(
            Ok: true,
            Command: "jobs cancel",
            Job: snapshot is null ? null : CliDtoMapper.FromSnapshot(snapshot)));
        return CliExitCode.Success;
    }

    public static async Task<int> JobsRetryAsync(CliCommandContext ctx, string jobIdText, CancellationToken ct)
    {
        if (!CliParsers.TryParseJobId(jobIdText, out var jobId, out var error))
        {
            return FailValidation(ctx, "jobs retry", error!);
        }

        await ctx.EnsureHostStartedAsync(ct).ConfigureAwait(false);
        MediaJobId newId;
        try
        {
            newId = await ctx.Jobs.RetryAsync(jobId, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            var invalid = ClipifyError.InvalidState(ex.Message);
            ctx.Renderer.WriteResult(new CliCommandResultDto(
                Ok: false,
                Command: "jobs retry",
                Error: CliDtoMapper.FromError(invalid)));
            return ExitCodeMapper.FromError(invalid);
        }

        ctx.AttachCancelKeyHandler();
        ctx.SetActiveJob(newId);
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, ctx.ProcessToken);
            var snapshot = await JobWaiter.WaitForTerminalAsync(
                    ctx.Jobs,
                    newId,
                    ctx.Renderer,
                    linked.Token,
                    ctx.Runtime.PollInterval)
                .ConfigureAwait(false);

            ctx.Renderer.WriteResult(new CliCommandResultDto(
                Ok: snapshot.State == MediaJobState.Succeeded,
                Command: "jobs retry",
                Job: CliDtoMapper.FromSnapshot(snapshot)));
            return ExitCodeMapper.FromJobSnapshot(snapshot);
        }
        finally
        {
            ctx.ClearActiveJob();
        }
    }

    private static async Task<int> RunMediaJobAsync(
        CliCommandContext ctx,
        string command,
        MediaJobDefinition definition,
        CancellationToken ct)
    {
        await ctx.EnsureHostStartedAsync(ct).ConfigureAwait(false);
        ctx.AttachCancelKeyHandler();

        MediaJobId jobId;
        try
        {
            jobId = await ctx.Jobs.EnqueueAsync(definition, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var error = ClipifyError.Internal("Failed to enqueue job.", ex.Message);
            ctx.Renderer.WriteResult(new CliCommandResultDto(
                Ok: false,
                Command: command,
                Error: CliDtoMapper.FromError(error)));
            return ExitCodeMapper.FromError(error);
        }

        ctx.SetActiveJob(jobId);
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, ctx.ProcessToken);
            var snapshot = await JobWaiter.WaitForTerminalAsync(
                    ctx.Jobs,
                    jobId,
                    ctx.Renderer,
                    linked.Token,
                    ctx.Runtime.PollInterval)
                .ConfigureAwait(false);

            var ok = snapshot.State == MediaJobState.Succeeded;
            ctx.Renderer.WriteResult(new CliCommandResultDto(
                Ok: ok,
                Command: command,
                Job: CliDtoMapper.FromSnapshot(snapshot),
                Error: ok || snapshot.ErrorCode is null
                    ? null
                    : new CliErrorDto(snapshot.ErrorCode, snapshot.ErrorMessage ?? "Job failed.")));
            return ExitCodeMapper.FromJobSnapshot(snapshot);
        }
        catch (OperationCanceledException)
        {
            var snapshot = await ctx.Jobs.GetAsync(jobId, CancellationToken.None).ConfigureAwait(false);
            if (snapshot is not null)
            {
                ctx.Renderer.WriteResult(new CliCommandResultDto(
                    Ok: false,
                    Command: command,
                    Job: CliDtoMapper.FromSnapshot(snapshot),
                    Error: new CliErrorDto("Canceled", "Command canceled.")));
                return snapshot.IsTerminal
                    ? ExitCodeMapper.FromJobSnapshot(snapshot)
                    : CliExitCode.Canceled;
            }

            return CliExitCode.Canceled;
        }
        finally
        {
            ctx.ClearActiveJob();
        }
    }

    private static int FailValidation(CliCommandContext ctx, string command, ClipifyError error)
    {
        ctx.Renderer.WriteResult(new CliCommandResultDto(
            Ok: false,
            Command: command,
            Error: CliDtoMapper.FromError(error)));
        return ExitCodeMapper.FromError(error);
    }
}
