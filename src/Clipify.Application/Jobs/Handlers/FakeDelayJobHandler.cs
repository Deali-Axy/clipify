using Clipify.Domain.Jobs;

namespace Clipify.Application.Jobs;

public sealed class MediaJobHandlerDispatcher : IMediaJobHandlerDispatcher
{
    private readonly IMediaJobHandler<FakeDelayJobDefinition> _fakeDelayHandler;

    public MediaJobHandlerDispatcher(IMediaJobHandler<FakeDelayJobDefinition> fakeDelayHandler)
    {
        _fakeDelayHandler = fakeDelayHandler;
    }

    public Task ExecuteAsync(
        MediaJobDefinition definition,
        MediaJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        return definition switch
        {
            FakeDelayJobDefinition fake => _fakeDelayHandler.ExecuteAsync(fake, context, cancellationToken),
            _ => throw new InvalidOperationException($"No handler registered for kind '{definition.Kind}'."),
        };
    }
}

public sealed class FakeDelayJobHandler : IMediaJobHandler<FakeDelayJobDefinition>
{
    private readonly TimeProvider _timeProvider;

    public FakeDelayJobHandler(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task ExecuteAsync(
        FakeDelayJobDefinition definition,
        MediaJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetUtcNow();
        await context.ReportProgressAsync(
                MediaJobProgress.Create("starting", started, fraction: 0, message: definition.Label),
                cancellationToken)
            .ConfigureAwait(false);

        if (definition.Delay > TimeSpan.Zero)
        {
            await Task.Delay(definition.Delay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (definition.Fail)
        {
            throw new InvalidOperationException(definition.Label ?? "FakeDelayJobHandler was configured to fail.");
        }

        var done = _timeProvider.GetUtcNow();
        await context.ReportProgressAsync(
                MediaJobProgress.Create("completed", done, fraction: 1, message: definition.Label),
                cancellationToken)
            .ConfigureAwait(false);

        await context.AddArtifactAsync(
                MediaArtifact.Create(
                    context.Snapshot.Id,
                    kind: "fake_output",
                    path: $"fake://{context.Snapshot.Id}/{definition.Label ?? "output"}",
                    createdAt: done,
                    sizeBytes: 0),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
