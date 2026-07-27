using Clipify.Domain.Jobs;

namespace Clipify.Application.Jobs;

/// <summary>
/// Bounded wake-up signal for workers. SQLite remains the source of truth.
/// </summary>
public interface IMediaJobQueue
{
    ValueTask NotifyAsync(MediaJobId jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until a wake signal arrives or the timeout elapses. Returns false on timeout.
    /// </summary>
    ValueTask<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
