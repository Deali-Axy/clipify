using Clipify.Application.Abstractions;
using Clipify.Application.Jobs;
using Clipify.Cli.Output;
using Clipify.Domain.Jobs;

namespace Clipify.Cli;

/// <summary>
/// Waits for a job terminal state via WatchAsync with persisted GetAsync fallback.
/// </summary>
public static class JobWaiter
{
    public static async Task<MediaJobSnapshot> WaitForTerminalAsync(
        IMediaJobService jobs,
        MediaJobId jobId,
        ICliRenderer renderer,
        CancellationToken cancellationToken,
        TimeSpan? pollInterval = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(500);

        var current = await jobs.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            throw new ClipifyException(
                ClipifyErrorCode.NotFound,
                $"Job not found: {jobId.Value}");
        }

        if (current.IsTerminal)
        {
            return current;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var watchTask = WatchLoopAsync(jobs, jobId, renderer, linked.Token);
        var pollTask = PollLoopAsync(jobs, jobId, renderer, interval, linked.Token);

        var completed = await Task.WhenAny(watchTask, pollTask).ConfigureAwait(false);
        linked.Cancel();

        // Wait for the losing branch to stop so it cannot emit progress after the final result
        // and so its exceptions are observed.
        try
        {
            await Task.WhenAll(watchTask, pollTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // Losing branch may fail after cancel; the winner's result is authoritative.
        }

        try
        {
            return await completed.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            var snapshot = await jobs.GetAsync(jobId, CancellationToken.None).ConfigureAwait(false);
            if (snapshot?.IsTerminal == true)
            {
                return snapshot;
            }

            throw;
        }
    }

    private static async Task<MediaJobSnapshot> WatchLoopAsync(
        IMediaJobService jobs,
        MediaJobId jobId,
        ICliRenderer renderer,
        CancellationToken cancellationToken)
    {
        await foreach (var change in jobs.WatchAsync(cancellationToken).ConfigureAwait(false))
        {
            if (change.JobId != jobId)
            {
                continue;
            }

            if (change.Progress is not null)
            {
                renderer.WriteProgress(CliDtoMapper.FromProgress(change.Progress)!, jobId.Value);
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
        ICliRenderer renderer,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        MediaJobProgress? lastProgress = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            var snapshot = await jobs.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                continue;
            }

            if (snapshot.Progress is not null
                && snapshot.Progress != lastProgress)
            {
                lastProgress = snapshot.Progress;
                renderer.WriteProgress(CliDtoMapper.FromProgress(snapshot.Progress)!, jobId.Value);
            }

            if (snapshot.IsTerminal)
            {
                return snapshot;
            }
        }

        throw new OperationCanceledException(cancellationToken);
    }
}
