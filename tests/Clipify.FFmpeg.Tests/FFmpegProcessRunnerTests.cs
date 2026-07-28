using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Clipify.Application.Abstractions;
using Clipify.Application.Media;
using Clipify.FFmpeg;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Clipify.FFmpeg.Tests;

public class FFmpegLocatorTests
{
    [Fact]
    public void Resolves_explicit_configured_path()
    {
        var ffmpeg = Path.GetTempFileName();
        var ffprobe = Path.GetTempFileName();
        try
        {
            var locator = new FFmpegLocator(Options.Create(new ClipifyFFmpegOptions
            {
                FFmpegPath = ffmpeg,
                FFprobePath = ffprobe,
            }));

            Assert.Equal(Path.GetFullPath(ffmpeg), locator.ResolveFFmpegPath());
            Assert.Equal(Path.GetFullPath(ffprobe), locator.ResolveFFprobePath());
        }
        finally
        {
            File.Delete(ffmpeg);
            File.Delete(ffprobe);
        }
    }

    [Fact]
    public void Prefers_local_tools_directory_over_path()
    {
        var dir = Directory.CreateTempSubdirectory("clipify-ffmpeg-tools-");
        try
        {
            var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
            var local = Path.Combine(dir.FullName, name);
            File.WriteAllText(local, "dummy");

            var locator = new FFmpegLocator(Options.Create(new ClipifyFFmpegOptions
            {
                LocalToolsDirectory = dir.FullName,
            }));

            Assert.Equal(Path.GetFullPath(local), locator.ResolveFFmpegPath());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Throws_when_configured_path_missing()
    {
        var locator = new FFmpegLocator(Options.Create(new ClipifyFFmpegOptions
        {
            FFmpegPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing-ffmpeg"),
        }));

        var ex = Assert.Throws<ClipifyException>(() => locator.ResolveFFmpegPath());
        Assert.Equal(ClipifyErrorCode.FfmpegUnavailable, ex.Code);
    }

    [Fact]
    public void Does_not_resolve_until_called()
    {
        // Constructing locator must not require ffmpeg on PATH / disk.
        var locator = new FFmpegLocator(Options.Create(new ClipifyFFmpegOptions
        {
            FFmpegPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "never-touched"),
        }));

        Assert.NotNull(locator);
    }
}

public class FFmpegProcessRunnerTests
{
    private static string ProcessHostPath
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            var candidate = Path.Combine(dir, "Clipify.FFmpeg.ProcessHost.dll");
            if (File.Exists(candidate))
            {
                // Prefer adjacent exe when published as framework-dependent companion.
            }

            var exeName = OperatingSystem.IsWindows()
                ? "Clipify.FFmpeg.ProcessHost.exe"
                : "Clipify.FFmpeg.ProcessHost";
            var exe = Path.Combine(dir, exeName);
            if (File.Exists(exe))
            {
                return exe;
            }

            // Fall back to `dotnet <dll>` by returning dll and letting callers use dotnet.
            var dll = Path.Combine(dir, "Clipify.FFmpeg.ProcessHost.dll");
            if (File.Exists(dll))
            {
                return dll;
            }

            throw new FileNotFoundException(
                "ProcessHost binary not found next to test assembly. Ensure the ProjectReference copies build output.",
                exe);
        }
    }

    private static (string Executable, IReadOnlyList<string> Args) Host(params string[] modeArgs)
    {
        var path = ProcessHostPath;
        if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var args = new List<string> { path };
            args.AddRange(modeArgs);
            return ("dotnet", args);
        }

        return (path, modeArgs);
    }

    private static FFmpegProcessRunner CreateRunner()
    {
        return new FFmpegProcessRunner(
            new SystemFFmpegProcessFactory(),
            Options.Create(new ClipifyFFmpegOptions { CancelGracePeriod = TimeSpan.FromMilliseconds(200) }),
            NullLogger<FFmpegProcessRunner>.Instance);
    }

    [Fact]
    public async Task Returns_non_zero_exit_code()
    {
        var (exe, args) = Host("exit", "7");
        var result = await CreateRunner().RunAsync(new FFmpegProcessSpec(exe, args));
        Assert.Equal(7, result.ExitCode);
        Assert.False(result.WasCanceled);
    }

    [Fact]
    public async Task Caps_long_stderr()
    {
        var (exe, args) = Host("stderr-flood", "100000");
        var result = await CreateRunner().RunAsync(new FFmpegProcessSpec(exe, args, StderrLogLimitChars: 4096));
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.StderrSummary.Length <= 4096);
    }

    [Fact]
    public async Task Drains_stdout_and_stderr_concurrently()
    {
        var (exe, args) = Host("dual-stream", "50");
        var progressCount = 0;
        var result = await CreateRunner().RunAsync(new FFmpegProcessSpec(
            exe,
            args,
            OnProgress: _ => Interlocked.Increment(ref progressCount)));

        Assert.Equal(0, result.ExitCode);
        Assert.True(progressCount >= 1);
        Assert.Contains("diag line", result.StderrSummary, StringComparison.Ordinal);
        Assert.NotNull(result.LastProgress);
        Assert.True(result.LastProgress!.IsEnd);
    }

    [Fact]
    public async Task Cancel_kills_process_tree_and_exits_reported_pids()
    {
        var pidFile = Path.Combine(Path.GetTempPath(), $"clipify-pids-{Guid.NewGuid():N}.txt");
        try
        {
            var (exe, args) = Host("child-tree", "60000", pidFile);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var runner = CreateRunner();

            var result = await runner.RunAsync(
                new FFmpegProcessSpec(exe, args, CancelGracePeriod: TimeSpan.FromMilliseconds(100)),
                cts.Token);

            Assert.True(result.WasCanceled);
            Assert.True(result.ProcessId > 0);
            AssertProcessExited(result.ProcessId);

            // Wait briefly for pid file flush from child-tree host.
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (!File.Exists(pidFile) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            if (File.Exists(pidFile))
            {
                var text = await File.ReadAllTextAsync(pidFile);
                foreach (Match match in Regex.Matches(text, @"(?:parent|child)=(\d+)"))
                {
                    AssertProcessExited(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
                }
            }
        }
        finally
        {
            if (File.Exists(pidFile))
            {
                File.Delete(pidFile);
            }
        }
    }

    private static void AssertProcessExited(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            // If we can open it, it must have already exited (zombie/handle) or still running.
            if (!process.HasExited)
            {
                Assert.Fail($"Process {pid} is still running after cancel.");
            }
        }
        catch (ArgumentException)
        {
            // Process no longer exists — expected.
        }
    }

    [Fact]
    public async Task Parses_progress_from_stdout_not_stderr()
    {
        var (exe, args) = Host("progress", "3", "20");
        var snapshots = new List<FFmpegProgressSnapshot>();
        var result = await CreateRunner().RunAsync(new FFmpegProcessSpec(
            exe,
            args,
            OnProgress: snapshots.Add));

        Assert.Equal(0, result.ExitCode);
        Assert.True(snapshots.Count >= 3);
        Assert.Contains(snapshots, s => s.IsEnd);
        Assert.All(snapshots.Where(s => s.OutTime is not null), s => Assert.True(s.OutTime >= TimeSpan.Zero));
    }
}
