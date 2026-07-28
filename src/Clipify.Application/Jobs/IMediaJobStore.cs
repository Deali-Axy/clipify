using Clipify.Domain.Jobs;

namespace Clipify.Application.Jobs;

/// <summary>
/// Persistence port for media jobs. Implementations must not expose EF types.
/// </summary>
public interface IMediaJobStore
{
    ValueTask InsertAsync(MediaJobSnapshot snapshot, CancellationToken cancellationToken = default);

    ValueTask<MediaJobSnapshot?> GetAsync(MediaJobId jobId, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<MediaJobSnapshot>> ListAsync(
        MediaJobListQuery query,
        CancellationToken cancellationToken = default);

    ValueTask UpdateProgressAsync(
        MediaJobId jobId,
        MediaJobProgress progress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts a conditional state transition. Returns true only when the row was still in <paramref name="from"/>.
    /// </summary>
    ValueTask<bool> TransitionAsync(
        MediaJobId jobId,
        MediaJobState from,
        MediaJobState to,
        DateTimeOffset updatedAt,
        string? errorCode = null,
        string? errorMessage = null,
        string? stage = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null,
        string? clearLeaseOwner = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically cancels or requests cancel based on the current persisted state.
    /// Queued → Canceled; Running → Canceling + CancelRequestedAt; Canceling → CancelRequestedAt only.
    /// </summary>
    ValueTask<MediaJobCancelOutcome> CancelAsync(
        MediaJobId jobId,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default);

    ValueTask<bool> IsCancelRequestedAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims the next FIFO queued job when under the concurrency limit.
    /// Returns null when none available.
    /// </summary>
    ValueTask<MediaJobSnapshot?> TryClaimNextAsync(
        MediaJobClaimOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    ValueTask<bool> HeartbeatAsync(
        MediaJobId jobId,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks Running/Canceling jobs with expired leases as Interrupted when their job lock is released.
    /// </summary>
    ValueTask<int> RepairExpiredLeasesAsync(
        DateTimeOffset now,
        Func<MediaJobId, string?, bool> isJobLockHeldByOwner,
        CancellationToken cancellationToken = default);

    ValueTask AddArtifactAsync(MediaArtifact artifact, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<MediaArtifact>> ListArtifactsAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default);
}
