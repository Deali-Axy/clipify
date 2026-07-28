using System.Diagnostics;
using System.Text;
using Clipify.FFmpeg;

namespace Clipify.Cli.Tests;

public sealed class TempDataDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("clipify-cli-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // ignore cleanup races on Windows
        }
    }
}

public sealed class CapturingWriters : IDisposable
{
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();

    public TextWriter Out => _out;
    public TextWriter Error => _error;

    public string StdOut => _out.ToString();
    public string StdErr => _error.ToString();

    public void Dispose()
    {
        _out.Dispose();
        _error.Dispose();
    }
}

public static class CliTestHost
{
    private static string? _ffmpegPath;
    private static string? _ffprobePath;
    private static string? _sourceVideo;
    private static readonly SemaphoreSlim InitLock = new(1, 1);

    public static async Task EnsureMediaFixtureAsync()
    {
        if (_sourceVideo is not null)
        {
            return;
        }

        await InitLock.WaitAsync();
        try
        {
            if (_sourceVideo is not null)
            {
                return;
            }

            _ffmpegPath = FFmpegLocator.FindOnPath(OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
            _ffprobePath = FFmpegLocator.FindOnPath(OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            if (_ffmpegPath is null || _ffprobePath is null)
            {
                throw new InvalidOperationException(
                    "FFmpeg/ffprobe required for Clipify.Cli.Tests media scenarios.");
            }

            var root = Directory.CreateTempSubdirectory("clipify-cli-shared-").FullName;
            _sourceVideo = System.IO.Path.Combine(root, "source.mp4");
            await GenerateSourceAsync(_sourceVideo);
        }
        finally
        {
            InitLock.Release();
        }
    }

    public static string SourceVideo
    {
        get
        {
            if (_sourceVideo is null)
            {
                throw new InvalidOperationException("Call EnsureMediaFixtureAsync first.");
            }

            return _sourceVideo;
        }
    }

    public static CliRuntimeOptions CreateRuntime(TempDataDirectory data, CapturingWriters writers) =>
        new()
        {
            Output = writers.Out,
            Error = writers.Error,
            DataDirectory = data.Path,
            EnableFileLogging = false,
            PollInterval = TimeSpan.FromMilliseconds(150),
            ConfigureFFmpeg = o =>
            {
                if (_ffmpegPath is not null)
                {
                    o.FFmpegPath = _ffmpegPath;
                }

                if (_ffprobePath is not null)
                {
                    o.FFprobePath = _ffprobePath;
                }

                o.CancelGracePeriod = TimeSpan.FromMilliseconds(300);
            },
        };

    public static async Task<int> RunAsync(
        string[] args,
        TempDataDirectory data,
        CapturingWriters writers,
        CancellationToken cancellationToken = default)
    {
        return await CliApp.RunAsync(args, CreateRuntime(data, writers), cancellationToken);
    }

    public static string ClipifyAssemblyPath => typeof(CliApp).Assembly.Location;

    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string[] args,
        TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(ClipifyAssemblyPath);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start clipify process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            throw new TimeoutException($"clipify process timed out. args={string.Join(' ', args)}");
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static async Task GenerateSourceAsync(string path)
    {
        var args = new[]
        {
            "-f", "lavfi", "-i", "testsrc=size=320x240:rate=25:duration=2",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac",
            "-shortest", "-y", path,
        };

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg.");
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0 || !File.Exists(path))
        {
            throw new InvalidOperationException($"lavfi fixture failed: {stderr}");
        }
    }
}
