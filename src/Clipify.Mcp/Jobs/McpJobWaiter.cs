using Clipify.Application.Abstractions;
using Clipify.Application.Jobs;
using Clipify.Domain.Jobs;

namespace Clipify.Mcp.Jobs;

/// <summary>
/// Limited wait with WatchAsync preferred and persisted GetAsync fallback.
/// Timeout returns the latest snapshot and is not a job failure.
/// </summary>
public static class McpJobWaiter
{
    public static async Task<(MediaJobSnapshot Snapshot, bool TimedOut)> WaitAsync(
        IMediaJobService jobs,
        MediaJobId jobId,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        TimeSpan? pollInterval = null)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ClipifyException(ClipifyErrorCode.Validation, "timeout_seconds must be positive.");
        }

        var interval = pollInterval ?? TimeSpan.FromMilliseconds(500);

        var current = await jobs.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            throw new ClipifyException(ClipifyErrorCode.NotFound, $"Job not found: {jobId.Value}");
        }

        if (current.IsTerminal)
        {
            return (current, TimedOut: false);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var workCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);
        var watchTask = WatchLoopAsync(jobs, jobId, workCts.Token);
        var pollTask = PollLoopAsync(jobs, jobId, interval, workCts.Token);

        try
        {
            var completed = await Task.WhenAny(watchTask, pollTask).ConfigureAwait(false);
            workCts.Cancel();

            try
            {
                await Task.WhenAll(watchTask, pollTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }

            var snapshot = await completed.ConfigureAwait(false);
            return (snapshot, TimedOut: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            var snapshot = await jobs.GetAsync(jobId, CancellationToken.None).ConfigureAwait(false)
                ?? throw new ClipifyException(ClipifyErrorCode.NotFound, $"Job not found: {jobId.Value}");
            return (snapshot, TimedOut: !snapshot.IsTerminal);
        }
    }

    private static async Task<MediaJobSnapshot> WatchLoopAsync(
        IMediaJobService jobs,
        MediaJobId jobId,
        CancellationToken cancellationToken)
    {
        await foreach (var change in jobs.WatchAsync(cancellationToken).ConfigureAwait(false))
        {
            if (change.JobId != jobId)
            {
                continue;
            }

            var snapshot = await jobs.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
            if (snapshot?.IsTerminal == true)
            {
                return snapshot;
            }
        }

        var final = await jobs.GetAsync(jobId, CancellationToken.None).ConfigureAwait(false);
        if (final?.IsTerminal == true)
        {
            return final;
        }

        throw new OperationCanceledException(cancellationToken);
    }

    private static async Task<MediaJobSnapshot> PollLoopAsync(
        IMediaJobService jobs,
        MediaJobId jobId,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            var snapshot = await jobs.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
            if (snapshot?.IsTerminal == true)
            {
                return snapshot;
            }
        }

        throw new OperationCanceledException(cancellationToken);
    }
}
