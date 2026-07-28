using Clipify.Domain.Jobs;

namespace Clipify.Application.Jobs;

public interface IMediaJobHandler<in TDefinition>
    where TDefinition : MediaJobDefinition
{
    Task ExecuteAsync(
        TDefinition definition,
        MediaJobExecutionContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// Non-generic dispatcher used by the executor to resolve handlers by whitelist kind.
/// </summary>
public interface IMediaJobHandlerDispatcher
{
    Task ExecuteAsync(
        MediaJobDefinition definition,
        MediaJobExecutionContext context,
        CancellationToken cancellationToken);
}

public interface IJobCancellationRegistry
{
    CancellationTokenSource Register(MediaJobId jobId);

    void Unregister(MediaJobId jobId);

    bool TryCancel(MediaJobId jobId);
}

/// <summary>
/// Cross-process exclusive evidence that a worker process still owns a running job.
/// Separate from the SQLite migration lock.
/// </summary>
public interface IJobLock
{
    /// <summary>Attempts to acquire an exclusive lock file for the job. Returns false if held.</summary>
    ValueTask<IAsyncDisposable?> TryAcquireAsync(MediaJobId jobId, CancellationToken cancellationToken = default);

    /// <summary>Returns true when another process still holds the lock for the given owner hint.</summary>
    bool IsHeld(MediaJobId jobId, string? leaseOwner);
}

public interface IMediaJobChangePublisher
{
    ValueTask PublishAsync(MediaJobChange change, CancellationToken cancellationToken = default);

    IAsyncEnumerable<MediaJobChange> WatchAsync(CancellationToken cancellationToken = default);
}
