using System.Threading.Channels;
using Clipify.Domain.Jobs;

namespace Clipify.Application.Jobs;

public sealed class ChannelMediaJobQueue : IMediaJobQueue
{
    private readonly Channel<MediaJobId> _channel;

    public ChannelMediaJobQueue(int capacity = 256)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _channel = Channel.CreateBounded<MediaJobId>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    public ValueTask NotifyAsync(MediaJobId jobId, CancellationToken cancellationToken = default)
    {
        _channel.Writer.TryWrite(jobId);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            _ = await _channel.Reader.ReadAsync(timeoutCts.Token).ConfigureAwait(false);

            // Drain additional wake signals so one WaitAsync covers a burst.
            while (_channel.Reader.TryRead(out _))
            {
            }

            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}

public sealed class InMemoryJobCancellationRegistry : IJobCancellationRegistry
{
    private readonly Dictionary<MediaJobId, CancellationTokenSource> _tokens = new();
    private readonly object _gate = new();

    public CancellationTokenSource Register(MediaJobId jobId)
    {
        lock (_gate)
        {
            if (_tokens.TryGetValue(jobId, out var existing))
            {
                existing.Dispose();
            }

            var cts = new CancellationTokenSource();
            _tokens[jobId] = cts;
            return cts;
        }
    }

    public void Unregister(MediaJobId jobId)
    {
        lock (_gate)
        {
            if (_tokens.Remove(jobId, out var cts))
            {
                cts.Dispose();
            }
        }
    }

    public bool TryCancel(MediaJobId jobId)
    {
        lock (_gate)
        {
            if (_tokens.TryGetValue(jobId, out var cts))
            {
                cts.Cancel();
                return true;
            }

            return false;
        }
    }
}

/// <summary>
/// Fan-out publisher: each <see cref="WatchAsync"/> subscriber gets an independent bounded channel.
/// </summary>
public sealed class ChannelMediaJobChangePublisher : IMediaJobChangePublisher
{
    private readonly object _gate = new();
    private readonly List<Channel<MediaJobChange>> _subscribers = [];

    public ValueTask PublishAsync(MediaJobChange change, CancellationToken cancellationToken = default)
    {
        Channel<MediaJobChange>[] snapshot;
        lock (_gate)
        {
            snapshot = _subscribers.ToArray();
        }

        foreach (var channel in snapshot)
        {
            channel.Writer.TryWrite(change);
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<MediaJobChange> WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<MediaJobChange>(new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        lock (_gate)
        {
            _subscribers.Add(channel);
        }

        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var change))
                {
                    yield return change;
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _subscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        }
    }
}
