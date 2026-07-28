using System.Diagnostics;
using System.Text;
using Clipify.Application.Abstractions;
using Clipify.Application.Media;
using Microsoft.Extensions.Logging;

namespace Clipify.FFmpeg;

public sealed class FFmpegVersionProbe : IFFmpegVersionProbe
{
    private readonly IFFmpegLocator _locator;
    private readonly ILogger<FFmpegVersionProbe> _logger;

    public FFmpegVersionProbe(IFFmpegLocator locator, ILogger<FFmpegVersionProbe> logger)
    {
        _locator = locator;
        _logger = logger;
    }

    public ValueTask<FFmpegToolVersion> ProbeFFmpegAsync(CancellationToken cancellationToken = default) =>
        ProbeAsync(_locator.ResolveFFmpegPath(), cancellationToken);

    public ValueTask<FFmpegToolVersion> ProbeFFprobeAsync(CancellationToken cancellationToken = default) =>
        ProbeAsync(_locator.ResolveFFprobePath(), cancellationToken);

    private async ValueTask<FFmpegToolVersion> ProbeAsync(string executablePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(executablePath))
        {
            throw new ClipifyException(
                ClipifyErrorCode.FfmpegUnavailable,
                $"Binary not found: {executablePath}");
        }

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-version");

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                throw new ClipifyException(
                    ClipifyErrorCode.FfmpegUnavailable,
                    $"Failed to start process: {executablePath}");
            }
        }
        catch (Exception ex) when (ex is not ClipifyException)
        {
            throw new ClipifyException(
                ClipifyErrorCode.FfmpegUnavailable,
                $"Binary is not executable: {executablePath}",
                detail: ex.Message,
                innerException: ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await TerminateAndWaitAsync(process).ConfigureAwait(false);
            throw;
        }

        var output = stdout.ToString();
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            _logger.LogWarning(
                "Version probe failed for {Path} with exit {ExitCode}. stderr: {Stderr}",
                executablePath,
                process.ExitCode,
                Truncate(stderr.ToString(), 2000));

            throw new ClipifyException(
                ClipifyErrorCode.FfmpegUnavailable,
                $"Version probe failed for '{executablePath}' (exit {process.ExitCode}).");
        }

        var firstLine = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?? output.Trim();

        return new FFmpegToolVersion(executablePath, firstLine, output);
    }

    private static async Task TerminateAndWaitAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // best-effort
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
