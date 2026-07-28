using System.Diagnostics;
using System.Text;
using Clipify.Application.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clipify.FFmpeg;

/// <summary>
/// Abstraction over Process start so runner tests can substitute a boundary.
/// </summary>
public interface IFFmpegProcessFactory
{
    IStartedFFmpegProcess Start(ProcessStartInfo startInfo);
}

public interface IStartedFFmpegProcess : IAsyncDisposable
{
    int Id { get; }

    StreamReader StandardOutput { get; }

    StreamReader StandardError { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken = default);

    bool HasExited { get; }

    int ExitCode { get; }

    void Kill(bool entireProcessTree);
}

public sealed class SystemFFmpegProcessFactory : IFFmpegProcessFactory
{
    public IStartedFFmpegProcess Start(ProcessStartInfo startInfo)
    {
        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"Failed to start process: {startInfo.FileName}");
        }

        return new SystemStartedFFmpegProcess(process);
    }

    private sealed class SystemStartedFFmpegProcess : IStartedFFmpegProcess
    {
        private readonly Process _process;

        public SystemStartedFFmpegProcess(Process process) => _process = process;

        public int Id => _process.Id;

        public StreamReader StandardOutput => _process.StandardOutput;

        public StreamReader StandardError => _process.StandardError;

        public bool HasExited => _process.HasExited;

        public int ExitCode => _process.ExitCode;

        public Task WaitForExitAsync(CancellationToken cancellationToken = default) =>
            _process.WaitForExitAsync(cancellationToken);

        public void Kill(bool entireProcessTree) => _process.Kill(entireProcessTree);

        public ValueTask DisposeAsync()
        {
            _process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class FFmpegProcessRunner : IFFmpegProcessRunner
{
    private readonly IFFmpegProcessFactory _processFactory;
    private readonly ClipifyFFmpegOptions _options;
    private readonly ILogger<FFmpegProcessRunner> _logger;

    public FFmpegProcessRunner(
        IFFmpegProcessFactory processFactory,
        IOptions<ClipifyFFmpegOptions> options,
        ILogger<FFmpegProcessRunner> logger)
    {
        _processFactory = processFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FFmpegProcessResult> RunAsync(
        FFmpegProcessSpec spec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.ExecutablePath);
        ArgumentNullException.ThrowIfNull(spec.Arguments);

        var psi = new ProcessStartInfo
        {
            FileName = spec.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in spec.Arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        await using var process = _processFactory.Start(psi);
        _logger.LogInformation(
            "Started process {ProcessId}: {Executable} ({ArgCount} args)",
            process.Id,
            spec.ExecutablePath,
            spec.Arguments.Count);

        var parser = new FFmpegProgressParser();
        var stderrLimit = spec.StderrLogLimitChars > 0
            ? spec.StderrLogLimitChars
            : _options.StderrLogLimitChars;
        var stderr = new StringBuilder(Math.Min(4096, stderrLimit));
        FFmpegProgressSnapshot? lastProgress = null;
        var wasCanceled = false;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stdoutTask = DrainStdoutAsync(process.StandardOutput, parser, spec.OnProgress, snapshot => lastProgress = snapshot, linkedCts.Token);
        var stderrTask = DrainStderrAsync(process.StandardError, stderr, stderrLimit, linkedCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            wasCanceled = true;
            await TerminateAsync(process, spec.CancelGracePeriod ?? _options.CancelGracePeriod).ConfigureAwait(false);
        }
        finally
        {
            await linkedCts.CancelAsync().ConfigureAwait(false);
            try
            {
                await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected when draining stops after cancel
            }
        }

        if (!process.HasExited)
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var stderrSummary = stderr.ToString();
        if (stderrSummary.Length > 0)
        {
            _logger.LogDebug(
                "Process {ProcessId} stderr summary ({Length} chars): {Summary}",
                process.Id,
                stderrSummary.Length,
                stderrSummary.Length > 500 ? stderrSummary[..500] : stderrSummary);
        }

        return new FFmpegProcessResult(
            process.ExitCode,
            wasCanceled,
            stderrSummary,
            lastProgress);
    }

    private async Task TerminateAsync(IStartedFFmpegProcess process, TimeSpan gracePeriod)
    {
        if (process.HasExited)
        {
            return;
        }

        _logger.LogInformation(
            "Cancel requested for process {ProcessId}; waiting {Grace} before kill.",
            process.Id,
            gracePeriod);

        try
        {
            using var graceCts = new CancellationTokenSource(gracePeriod);
            await process.WaitForExitAsync(graceCts.Token).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException)
        {
        }

        if (process.HasExited)
        {
            return;
        }

        _logger.LogWarning("Killing process tree for {ProcessId}.", process.Id);
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // already exited
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Waiting after kill for process {ProcessId} failed.", process.Id);
        }
    }

    private static async Task DrainStdoutAsync(
        StreamReader reader,
        FFmpegProgressParser parser,
        Action<FFmpegProgressSnapshot>? onProgress,
        Action<FFmpegProgressSnapshot> setLast,
        CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            parser.Append(new string(buffer, 0, read));
            while (parser.TryDequeueSnapshot(out var snapshot))
            {
                setLast(snapshot);
                onProgress?.Invoke(snapshot);
            }
        }
    }

    private static async Task DrainStderrAsync(
        StreamReader reader,
        StringBuilder sink,
        int limit,
        CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (sink.Length >= limit)
            {
                continue;
            }

            var remaining = limit - sink.Length;
            sink.Append(buffer, 0, Math.Min(read, remaining));
        }
    }
}
