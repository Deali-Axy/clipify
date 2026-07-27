using Clipify.Domain.Jobs;

namespace Clipify.Application.Jobs;

/// <summary>
/// Executes claimed jobs: progress/artifacts, cancel polling, exception isolation.
/// Hosting wraps this in a BackgroundService.
/// </summary>
public sealed class MediaJobExecutor
{
    private readonly IMediaJobStore _store;
    private readonly IMediaJobQueue _queue;
    private readonly IMediaJobHandlerDispatcher _dispatcher;
    private readonly IJobCancellationRegistry _cancellation;
    private readonly IJobLock _jobLock;
    private readonly IMediaJobChangePublisher _changes;
    private readonly TimeProvider _timeProvider;
    private readonly MediaJobWorkerOptions _options;

    public MediaJobExecutor(
        IMediaJobStore store,
        IMediaJobQueue queue,
        IMediaJobHandlerDispatcher dispatcher,
        IJobCancellationRegistry cancellation,
        IJobLock jobLock,
        IMediaJobChangePublisher changes,
        MediaJobWorkerOptions options,
        TimeProvider? timeProvider = null)
    {
        _store = store;
        _queue = queue;
        _dispatcher = dispatcher;
        _cancellation = cancellation;
        _jobLock = jobLock;
        _changes = changes;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        await RepairAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(_options.PollInterval, _timeProvider);

        // Kick once immediately so queued jobs run without waiting for the first tick.
        await PumpAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            var wakeTask = _queue.WaitAsync(_options.PollInterval, stoppingToken).AsTask();
            var tickTask = timer.WaitForNextTickAsync(stoppingToken).AsTask();
            _ = await Task.WhenAny(wakeTask, tickTask).ConfigureAwait(false);

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await PumpAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    public async Task RepairAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        await _store.RepairExpiredLeasesAsync(
                now,
                (jobId, owner) => _jobLock.IsHeld(jobId, owner),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task PumpAsync(CancellationToken cancellationToken = default)
    {
        await RepairAsync(cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = _timeProvider.GetUtcNow();
            var claimed = await _store.TryClaimNextAsync(
                    new MediaJobClaimOptions(
                        _options.LeaseOwner,
                        _options.LeaseDuration,
                        _options.MaxConcurrency),
                    now,
                    cancellationToken)
                .ConfigureAwait(false);

            if (claimed is null)
            {
                return;
            }

            // Fire-and-forget within the process; exceptions are isolated per job.
            _ = ExecuteClaimedJobAsync(claimed, cancellationToken);
        }
    }

    private async Task ExecuteClaimedJobAsync(MediaJobSnapshot claimed, CancellationToken hostToken)
    {
        var jobLock = await _jobLock.TryAcquireAsync(claimed.Id, hostToken).ConfigureAwait(false);
        if (jobLock is null)
        {
            // Another process still holds the OS lock; leave lease expiry / repair to handle it.
            return;
        }

        await using (jobLock.ConfigureAwait(false))
        {
            var jobCts = _cancellation.Register(claimed.Id);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(hostToken, jobCts.Token);

            try
            {
                await _changes.PublishAsync(
                        new MediaJobChange(claimed.Id, MediaJobState.Running, _timeProvider.GetUtcNow()),
                        CancellationToken.None)
                    .ConfigureAwait(false);

                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                var heartbeat = HeartbeatLoopAsync(claimed.Id, heartbeatCts.Token);

                try
                {
                    var context = new MediaJobExecutionContext
                    {
                        Snapshot = claimed,
                        ReportProgressAsync = async (progress, ct) =>
                        {
                            await _store.UpdateProgressAsync(claimed.Id, progress, ct).ConfigureAwait(false);
                            await _changes.PublishAsync(
                                    new MediaJobChange(claimed.Id, MediaJobState.Running, progress.UpdatedAt, progress),
                                    ct)
                                .ConfigureAwait(false);
                        },
                        AddArtifactAsync = (artifact, ct) => _store.AddArtifactAsync(artifact, ct),
                    };

                    await _dispatcher.ExecuteAsync(claimed.Definition, context, linked.Token)
                        .ConfigureAwait(false);

                    await heartbeatCts.CancelAsync().ConfigureAwait(false);
                    try
                    {
                        await heartbeat.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    var completedAt = _timeProvider.GetUtcNow();
                    if (await _store.IsCancelRequestedAsync(claimed.Id, CancellationToken.None).ConfigureAwait(false)
                        || linked.IsCancellationRequested)
                    {
                        await CompleteAsCanceledAsync(claimed.Id, completedAt).ConfigureAwait(false);
                    }
                    else
                    {
                        await _store.TransitionAsync(
                                claimed.Id,
                                MediaJobState.Running,
                                MediaJobState.Succeeded,
                                completedAt,
                                completedAt: completedAt,
                                clearLeaseOwner: _options.LeaseOwner,
                                cancellationToken: CancellationToken.None)
                            .ConfigureAwait(false);

                        await _changes.PublishAsync(
                                new MediaJobChange(claimed.Id, MediaJobState.Succeeded, completedAt),
                                CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    await heartbeatCts.CancelAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException oce)
            {
                var cancelRequested = jobCts.IsCancellationRequested
                    || await _store.IsCancelRequestedAsync(claimed.Id, CancellationToken.None).ConfigureAwait(false);

                if (cancelRequested)
                {
                    await CompleteAsCanceledAsync(claimed.Id, _timeProvider.GetUtcNow()).ConfigureAwait(false);
                }
                else if (!hostToken.IsCancellationRequested)
                {
                    throw;
                }
                else
                {
                    // Host is stopping; leave state for lease repair / Interrupted.
                    _ = oce;
                }
            }
            catch (Exception ex)
            {
                var failedAt = _timeProvider.GetUtcNow();
                var current = await _store.GetAsync(claimed.Id, CancellationToken.None).ConfigureAwait(false);
                var from = current?.State ?? MediaJobState.Running;

                if (from is MediaJobState.Running or MediaJobState.Canceling)
                {
                    await _store.TransitionAsync(
                            claimed.Id,
                            from,
                            MediaJobState.Failed,
                            failedAt,
                            errorCode: Clipify.Application.Abstractions.ClipifyErrorCode.HandlerFailed.ToString(),
                            errorMessage: ex.Message,
                            completedAt: failedAt,
                            clearLeaseOwner: _options.LeaseOwner,
                            cancellationToken: CancellationToken.None)
                        .ConfigureAwait(false);

                    await _changes.PublishAsync(
                            new MediaJobChange(
                                claimed.Id,
                                MediaJobState.Failed,
                                failedAt,
                                ErrorCode: Clipify.Application.Abstractions.ClipifyErrorCode.HandlerFailed.ToString(),
                                ErrorMessage: ex.Message),
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                _cancellation.Unregister(claimed.Id);
            }
        }
    }

    private async Task CompleteAsCanceledAsync(MediaJobId jobId, DateTimeOffset at)
    {
        var current = await _store.GetAsync(jobId, CancellationToken.None).ConfigureAwait(false);
        if (current is null || MediaJobStateTransitions.IsTerminal(current.State))
        {
            return;
        }

        if (current.State == MediaJobState.Running)
        {
            await _store.TransitionAsync(
                    jobId,
                    MediaJobState.Running,
                    MediaJobState.Canceling,
                    at,
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }

        var from = current.State == MediaJobState.Running ? MediaJobState.Canceling : current.State;
        if (from is MediaJobState.Canceling or MediaJobState.Queued)
        {
            await _store.TransitionAsync(
                    jobId,
                    from,
                    MediaJobState.Canceled,
                    at,
                    completedAt: at,
                    clearLeaseOwner: _options.LeaseOwner,
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);

            await _changes.PublishAsync(
                    new MediaJobChange(jobId, MediaJobState.Canceled, at),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private async Task HeartbeatLoopAsync(MediaJobId jobId, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.HeartbeatInterval, _timeProvider);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var now = _timeProvider.GetUtcNow();
            var ok = await _store.HeartbeatAsync(
                    jobId,
                    _options.LeaseOwner,
                    now,
                    _options.LeaseDuration,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!ok)
            {
                return;
            }

            if (await _store.IsCancelRequestedAsync(jobId, cancellationToken).ConfigureAwait(false))
            {
                _cancellation.TryCancel(jobId);
            }
        }
    }
}

public sealed class MediaJobWorkerOptions
{
    public required string LeaseOwner { get; init; }

    public int MaxConcurrency { get; init; } = 1;

    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(LeaseOwner);
        if (MaxConcurrency is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrency), MaxConcurrency, "MaxConcurrency must be 1 or 2.");
        }
    }
}
