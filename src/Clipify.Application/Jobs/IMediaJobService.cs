using Clipify.Domain.Jobs;

namespace Clipify.Application.Jobs;

public interface IMediaJobService
{
    ValueTask<MediaJobId> EnqueueAsync(
        MediaJobDefinition definition,
        CancellationToken cancellationToken = default);

    ValueTask RequestCancelAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default);

    ValueTask<MediaJobId> RetryAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default);

    ValueTask<MediaJobSnapshot?> GetAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<MediaJobSnapshot>> ListAsync(
        MediaJobListQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<MediaArtifact>> ListArtifactsAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<MediaJobChange> WatchAsync(
        CancellationToken cancellationToken = default);
}
