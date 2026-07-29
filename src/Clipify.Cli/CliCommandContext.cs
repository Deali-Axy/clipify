using Clipify.Application.Jobs;
using Clipify.Cli.Output;
using Clipify.Domain.Jobs;
using Clipify.Hosting;
using Clipify.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Clipify.Application.Abstractions;

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
    private bool _workerStarted;
    private bool _cancelHooked;

    public required ICliRenderer Renderer { get; init; }
    public required OutputMode OutputMode { get; init; }
    public required CliRuntimeOptions Runtime { get; init; }
    public required string? DataDirectory { get; init; }

    public CancellationToken ProcessToken => _processCts.Token;

    public IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException("Host has not been created.");

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

    private ClipifyHostOptions CreateHostOptions() => new()
    {
        DataDirectory = DataDirectory ?? Runtime.DataDirectory,
        // CLI owns stdout via Renderer; never let Host/EF console logs pollute it.
        SuppressConsoleLogging = true,
        EnableFileLogging = Runtime.EnableFileLogging,
        PollInterval = Runtime.PollInterval ?? TimeSpan.FromSeconds(2),
        ConfigureFFmpeg = Runtime.ConfigureFFmpeg,
    };

    /// <summary>
    /// Builds DI without migrating or starting the Worker (for <c>doctor</c>).
    /// </summary>
    public void EnsureDiagnosticsHost()
    {
        if (_host is not null)
        {
            return;
        }

        _host = ClipifyHostFactory.BuildForDiagnostics(CreateHostOptions());
    }

    /// <summary>
    /// Builds host, migrates DB, starts Worker. Must be called before Application services that need the job loop.
    /// </summary>
    public async Task EnsureHostStartedAsync(CancellationToken cancellationToken = default)
    {
        if (_workerStarted)
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _processCts.Token);

        var options = CreateHostOptions();
        var paths = options.ResolvePaths();
        if (!ClipifyDoctor.TryPrepareDataRoot(paths, out var dataDirectoryCheck))
        {
            throw new ClipifyException(
                ClipifyErrorCode.Internal,
                dataDirectoryCheck.Message,
                dataDirectoryCheck.Detail);
        }

        if (_host is null)
        {
            try
            {
                _host = await ClipifyHostFactory.StartAsync(options, linked.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not ClipifyException)
            {
                throw new ClipifyException(
                    ClipifyErrorCode.Internal,
                    "Failed to start Clipify host.",
                    ex.Message,
                    ex);
            }

            _workerStarted = true;
            return;
        }

        // Diagnostics host already built — migrate and start Worker now.
        try
        {
            await _host.Services.MigrateClipifyDatabaseAsync(cancellationToken: linked.Token)
                .ConfigureAwait(false);
            await _host.StartAsync(linked.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ClipifyException)
        {
            throw new ClipifyException(
                ClipifyErrorCode.Internal,
                "Failed to start Clipify host.",
                ex.Message,
                ex);
        }

        _workerStarted = true;
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
                if (_workerStarted)
                {
                    await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
            catch
            {
                // Best-effort shutdown.
            }

            _host.Dispose();
            _host = null;
            _workerStarted = false;
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
