using Clipify.Application.Jobs;
using Clipify.Cli.Output;
using Clipify.Domain.Jobs;
using Clipify.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Clipify.Cli;

/// <summary>
/// Runtime knobs for production and tests (streams, data dir, FFmpeg paths).
/// </summary>
public sealed class CliRuntimeOptions
{
    public TextWriter? Output { get; init; }
    public TextWriter? Error { get; init; }
    public string? DataDirectory { get; init; }
    public Action<FFmpeg.ClipifyFFmpegOptions>? ConfigureFFmpeg { get; init; }
    public TimeSpan? PollInterval { get; init; }
    public bool EnableFileLogging { get; init; } = true;
}

public sealed class CliCommandContext : IAsyncDisposable
{
    private readonly CancellationTokenSource _processCts = new();
    private int _interruptCount;
    private MediaJobId? _activeJobId;
    private IHost? _host;
    private bool _cancelHooked;

    public required ICliRenderer Renderer { get; init; }
    public required OutputMode OutputMode { get; init; }
    public required CliRuntimeOptions Runtime { get; init; }
    public required string? DataDirectory { get; init; }

    public CancellationToken ProcessToken => _processCts.Token;

    public IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException("Host has not been started.");

    public IMediaJobService Jobs => Services.GetRequiredService<IMediaJobService>();

    public void AttachCancelKeyHandler()
    {
        if (_cancelHooked)
        {
            return;
        }

        Console.CancelKeyPress += OnCancelKeyPress;
        _cancelHooked = true;
    }

    public void SetActiveJob(MediaJobId jobId) => _activeJobId = jobId;

    public void ClearActiveJob() => _activeJobId = null;

    /// <summary>
    /// Builds host, migrates DB, starts Worker. Must be called before Application services are used.
    /// </summary>
    public async Task EnsureHostStartedAsync(CancellationToken cancellationToken = default)
    {
        if (_host is not null)
        {
            return;
        }

        var hostOptions = new ClipifyHostOptions
        {
            DataDirectory = DataDirectory ?? Runtime.DataDirectory,
            SuppressConsoleLogging = OutputMode is OutputMode.Json or OutputMode.Jsonl,
            EnableFileLogging = Runtime.EnableFileLogging,
            PollInterval = Runtime.PollInterval ?? TimeSpan.FromSeconds(2),
            ConfigureFFmpeg = Runtime.ConfigureFFmpeg,
        };

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _processCts.Token);
        _host = await ClipifyHostFactory.StartAsync(hostOptions, linked.Token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_cancelHooked)
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            _cancelHooked = false;
        }

        if (_host is not null)
        {
            try
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort shutdown.
            }

            _host.Dispose();
            _host = null;
        }

        _processCts.Dispose();
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        var count = Interlocked.Increment(ref _interruptCount);
        if (count == 1)
        {
            e.Cancel = true;
            Renderer.WriteDiagnostic("Cancellation requested. Waiting for cleanup…");
            _ = RequestCancelActiveJobAsync();
            return;
        }

        // Second interrupt: allow process to exit quickly.
        e.Cancel = false;
        _processCts.Cancel();
    }

    private async Task RequestCancelActiveJobAsync()
    {
        try
        {
            if (_host is null || _activeJobId is null)
            {
                _processCts.Cancel();
                return;
            }

            await Jobs.RequestCancelAsync(_activeJobId.Value, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Renderer.WriteWarning($"Failed to request cancel: {ex.Message}");
            _processCts.Cancel();
        }
    }
}
