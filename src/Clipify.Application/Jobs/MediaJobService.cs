using Clipify.Domain.Jobs;

namespace Clipify.Application.Jobs;

public sealed class MediaJobService : IMediaJobService
{
    private readonly IMediaJobStore _store;
    private readonly IMediaJobQueue _queue;
    private readonly IMediaJobChangePublisher _changes;
    private readonly IJobCancellationRegistry _cancellation;
    private readonly TimeProvider _timeProvider;

    public MediaJobService(
        IMediaJobStore store,
        IMediaJobQueue queue,
        IMediaJobChangePublisher changes,
        IJobCancellationRegistry cancellation,
        TimeProvider? timeProvider = null)
    {
        _store = store;
        _queue = queue;
        _changes = changes;
        _cancellation = cancellation;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<MediaJobId> EnqueueAsync(
        MediaJobDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!MediaJobDefinitionSerializer.IsAllowedKind(definition.Kind))
        {
            throw new ArgumentException(
                $"Definition kind '{definition.Kind}' is not allowed.",
                nameof(definition));
        }

        var now = _timeProvider.GetUtcNow();
        var id = MediaJobId.New();
        var snapshot = MediaJobSnapshot.CreateQueued(id, definition, now);

        // Persist first, then wake workers. Channel is not the source of truth.
        await _store.InsertAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await _changes.PublishAsync(
                new MediaJobChange(id, MediaJobState.Queued, now),
                cancellationToken)
            .ConfigureAwait(false);
        await _queue.NotifyAsync(id, cancellationToken).ConfigureAwait(false);
        return id;
    }

    public async ValueTask RequestCancelAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var snapshot = await _store.GetAsync(jobId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Job '{jobId}' was not found.");

        if (MediaJobStateTransitions.IsTerminal(snapshot.State))
        {
            return;
        }

        if (snapshot.State == MediaJobState.Queued)
        {
            await _store.TransitionAsync(
                    jobId,
                    MediaJobState.Queued,
                    MediaJobState.Canceled,
                    now,
                    completedAt: now,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await _changes.PublishAsync(
                    new MediaJobChange(jobId, MediaJobState.Canceled, now),
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await _store.RequestCancelAsync(jobId, now, cancellationToken).ConfigureAwait(false);
        _cancellation.TryCancel(jobId);

        if (snapshot.State == MediaJobState.Running)
        {
            await _store.TransitionAsync(
                    jobId,
                    MediaJobState.Running,
                    MediaJobState.Canceling,
                    now,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await _changes.PublishAsync(
                    new MediaJobChange(jobId, MediaJobState.Canceling, now),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask<MediaJobId> RetryAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default)
    {
        var original = await _store.GetAsync(jobId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Job '{jobId}' was not found.");

        if (!MediaJobStateTransitions.CanRetryFrom(original.State))
        {
            throw new InvalidOperationException(
                $"Job '{jobId}' in state '{original.State}' cannot be retried.");
        }

        var now = _timeProvider.GetUtcNow();
        var newId = MediaJobId.New();
        var snapshot = MediaJobSnapshot.CreateQueued(
            newId,
            original.Definition,
            now,
            retryOfJobId: original.Id);

        await _store.InsertAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await _changes.PublishAsync(
                new MediaJobChange(newId, MediaJobState.Queued, now),
                cancellationToken)
            .ConfigureAwait(false);
        await _queue.NotifyAsync(newId, cancellationToken).ConfigureAwait(false);
        return newId;
    }

    public ValueTask<MediaJobSnapshot?> GetAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default) =>
        _store.GetAsync(jobId, cancellationToken);

    public ValueTask<IReadOnlyList<MediaJobSnapshot>> ListAsync(
        MediaJobListQuery query,
        CancellationToken cancellationToken = default) =>
        _store.ListAsync(query, cancellationToken);

    public ValueTask<IReadOnlyList<MediaArtifact>> ListArtifactsAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default) =>
        _store.ListArtifactsAsync(jobId, cancellationToken);

    public IAsyncEnumerable<MediaJobChange> WatchAsync(CancellationToken cancellationToken = default) =>
        _changes.WatchAsync(cancellationToken);
}
