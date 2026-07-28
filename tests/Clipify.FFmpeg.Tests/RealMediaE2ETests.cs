using System.Diagnostics;
using Clipify.Application.Jobs;
using Clipify.Application.Media;
using Clipify.Domain.Jobs;
using Clipify.Domain.Media;
using Clipify.FFmpeg;
using Clipify.Hosting;
using Clipify.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clipify.FFmpeg.Tests;

/// <summary>
/// Real FFmpeg/ffprobe end-to-end tests. Missing binaries fail the suite with diagnostics (no silent skip).
/// </summary>
public class RealMediaE2ETests : IAsyncLifetime
{
    private static string? _ffmpegPath;
    private static string? _ffprobePath;
    private static string? _sourceVideo;
    private static string? _sharedRoot;

    public async Task InitializeAsync()
    {
        if (_sourceVideo is not null)
        {
            return;
        }

        EnsureTools();
        _sharedRoot = Directory.CreateTempSubdirectory("clipify-e2e-shared-").FullName;
        _sourceVideo = Path.Combine(_sharedRoot, "source.mp4");
        await GenerateSourceAsync(_sourceVideo);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void Tools_are_available_with_versions()
    {
        EnsureTools();
        var ffmpegVersion = RunCapture(_ffmpegPath!, ["-version"]);
        var ffprobeVersion = RunCapture(_ffprobePath!, ["-version"]);
        Assert.Contains("ffmpeg version", ffmpegVersion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ffprobe version", ffprobeVersion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Trim_extract_audio_and_thumbnail_succeed_with_artifacts()
    {
        await InitializeAsync();
        using var workspace = new TempWorkspace();
        using var host = await StartHostAsync(workspace);

        var jobs = host.Services.GetRequiredService<IMediaJobService>();
        var trimOut = Path.Combine(workspace.Path, "trim.mp4");
        var audioOut = Path.Combine(workspace.Path, "audio.mp3");
        var thumbOut = Path.Combine(workspace.Path, "thumb.jpg");

        var trimId = await jobs.EnqueueAsync(new TrimMediaJobDefinition
        {
            InputPath = _sourceVideo!,
            OutputPath = trimOut,
            Range = TimeRange.FromMilliseconds(0, 1000),
            ConflictPolicy = OutputConflictPolicy.Fail,
        });

        var audioId = await jobs.EnqueueAsync(new ExtractAudioJobDefinition
        {
            InputPath = _sourceVideo!,
            OutputPath = audioOut,
            Format = AudioOutputFormat.Mp3,
            ConflictPolicy = OutputConflictPolicy.Fail,
        });

        var thumbId = await jobs.EnqueueAsync(new ThumbnailJobDefinition
        {
            InputPath = _sourceVideo!,
            OutputPath = thumbOut,
            At = TimeSpan.FromMilliseconds(500),
            Format = ThumbnailImageFormat.Jpg,
            ConflictPolicy = OutputConflictPolicy.Fail,
        });

        await WaitSucceededAsync(jobs, trimId);
        await WaitSucceededAsync(jobs, audioId);
        await WaitSucceededAsync(jobs, thumbId);

        Assert.True(File.Exists(trimOut));
        Assert.True(File.Exists(audioOut));
        Assert.True(File.Exists(thumbOut));
        Assert.True(new FileInfo(trimOut).Length > 0);
        Assert.True(new FileInfo(audioOut).Length > 0);
        Assert.True(new FileInfo(thumbOut).Length > 0);

        var probe = host.Services.GetRequiredService<IProbeMediaUseCase>();
        var trimInfo = await probe.ExecuteAsync(trimOut);
        Assert.True(trimInfo.IsSuccess);
        Assert.NotNull(trimInfo.Value!.Duration);
        Assert.True(
            (trimInfo.Value.Duration.Value - TimeSpan.FromSeconds(1)).Duration() < TimeSpan.FromMilliseconds(750),
            $"trim duration {trimInfo.Value.Duration}");

        var audioInfo = await probe.ExecuteAsync(audioOut);
        Assert.True(audioInfo.IsSuccess);
        Assert.Contains(audioInfo.Value!.Streams, s => s.CodecType == "audio");

        var thumbInfo = await probe.ExecuteAsync(thumbOut);
        Assert.True(thumbInfo.IsSuccess);
        Assert.Contains(thumbInfo.Value!.Streams, s => s.CodecType == "video");

        Assert.Single(await jobs.ListArtifactsAsync(trimId));
        Assert.Single(await jobs.ListArtifactsAsync(audioId));
        Assert.Single(await jobs.ListArtifactsAsync(thumbId));
        Assert.Empty(Directory.GetFiles(workspace.Path, "*.partial*"));

        await host.StopAsync();
    }

    [Fact]
    public async Task Chinese_path_roundtrip_works()
    {
        await InitializeAsync();
        using var workspace = new TempWorkspace();
        var chineseDir = Path.Combine(workspace.Path, "中文 目录");
        Directory.CreateDirectory(chineseDir);
        var input = Path.Combine(chineseDir, "源 视频.mp4");
        File.Copy(_sourceVideo!, input);
        var output = Path.Combine(chineseDir, "裁剪 结果.mp4");

        using var host = await StartHostAsync(workspace);
        var jobs = host.Services.GetRequiredService<IMediaJobService>();
        var jobId = await jobs.EnqueueAsync(new TrimMediaJobDefinition
        {
            InputPath = input,
            OutputPath = output,
            Range = TimeRange.FromMilliseconds(200, 800),
        });

        await WaitSucceededAsync(jobs, jobId);
        Assert.True(File.Exists(output));
        Assert.True(new FileInfo(output).Length > 0);
        await host.StopAsync();
    }

    [Fact]
    public async Task Cancel_leaves_no_final_or_partial_output_and_ffmpeg_pids_exit()
    {
        await InitializeAsync();
        using var workspace = new TempWorkspace();
        var longSource = Path.Combine(workspace.Path, "long.mp4");
        // Long enough that cancel has a reliable observation window on fast CI runners.
        await GenerateSourceAsync(longSource, durationSeconds: 20);

        using var host = await StartHostAsync(workspace);
        var jobs = host.Services.GetRequiredService<IMediaJobService>();
        var output = Path.Combine(workspace.Path, "cancel-out.mp3");

        var baselinePids = SnapshotFfmpegPids();

        // Use extract-audio (re-encode) so cancel has a real window; stream-copy trim is too fast.
        var jobId = await jobs.EnqueueAsync(new ExtractAudioJobDefinition
        {
            InputPath = longSource,
            OutputPath = output,
            Format = AudioOutputFormat.Mp3,
        });

        // Wait until the job is running AND an ffmpeg process for this job has appeared.
        await WaitForAsync(async () =>
        {
            var snap = await jobs.GetAsync(jobId);
            if (snap?.State is MediaJobState.Failed)
            {
                throw new InvalidOperationException($"Job failed before cancel: {snap.ErrorCode} {snap.ErrorMessage}");
            }

            if (snap?.State is MediaJobState.Succeeded or MediaJobState.Canceled)
            {
                throw new InvalidOperationException(
                    $"Job reached {snap.State} before cancel window; ffmpeg pids={string.Join(',', SnapshotFfmpegPids())}");
            }

            if (snap?.State is not (MediaJobState.Running or MediaJobState.Canceling))
            {
                return false;
            }

            return SnapshotFfmpegPids().Except(baselinePids).Any();
        }, TimeSpan.FromSeconds(30));

        var startedPids = SnapshotFfmpegPids().Except(baselinePids).ToArray();
        Assert.NotEmpty(startedPids);

        await jobs.RequestCancelAsync(jobId);

        await WaitForAsync(async () =>
        {
            var snap = await jobs.GetAsync(jobId);
            return snap?.State == MediaJobState.Canceled;
        }, TimeSpan.FromSeconds(30));

        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(workspace.Path, "*.partial*", SearchOption.AllDirectories));

        foreach (var pid in startedPids)
        {
            AssertProcessExited(pid);
        }

        await host.StopAsync();
    }

    private static HashSet<int> SnapshotFfmpegPids()
    {
        // Prefer pgrep on Unix: Process.GetProcesses()+ProcessName is slow/unreliable on macOS CI
        // and can miss the entire FFmpeg lifetime of a short job.
        if (!OperatingSystem.IsWindows())
        {
            return SnapshotFfmpegPidsUnix();
        }

        var set = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.ProcessName.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase))
                {
                    set.Add(process.Id);
                }
            }
            catch
            {
                // ignore access races
            }
            finally
            {
                process.Dispose();
            }
        }

        return set;
    }

    private static HashSet<int> SnapshotFfmpegPidsUnix()
    {
        var set = new HashSet<int>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pgrep",
                ArgumentList = { "-x", "ffmpeg" },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return set;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            foreach (var line in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(line.Trim(), out var pid))
                {
                    set.Add(pid);
                }
            }
        }
        catch
        {
            // Fall back below if pgrep is unavailable.
        }

        if (set.Count > 0)
        {
            return set;
        }

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.ProcessName.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase))
                {
                    set.Add(process.Id);
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return set;
    }

    private static void AssertProcessExited(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
            {
                Assert.Fail($"FFmpeg process {pid} is still running after cancel.");
            }
        }
        catch (ArgumentException)
        {
            // exited and removed from process table
        }
    }

    [Fact]
    public async Task Probe_use_case_returns_domain_media_info()
    {
        await InitializeAsync();
        using var workspace = new TempWorkspace();
        using var host = await StartHostAsync(workspace);
        var probe = host.Services.GetRequiredService<IProbeMediaUseCase>();
        var result = await probe.ExecuteAsync(_sourceVideo!);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value!.Duration > TimeSpan.Zero);
        Assert.Contains(result.Value.Streams, s => s.CodecType == "video");
        await host.StopAsync();
    }

    private static void EnsureTools()
    {
        if (_ffmpegPath is not null && _ffprobePath is not null)
        {
            return;
        }

        _ffmpegPath = FFmpegLocator.FindOnPath(OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        _ffprobePath = FFmpegLocator.FindOnPath(OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");

        if (_ffmpegPath is null || _ffprobePath is null)
        {
            throw new InvalidOperationException(
                "FFmpeg/ffprobe are required for Clipify.FFmpeg.Tests real media E2E. " +
                "Install both on PATH (or configure CI to provide them). " +
                $"ffmpeg={_ffmpegPath ?? "<missing>"}, ffprobe={_ffprobePath ?? "<missing>"}.");
        }
    }

    private static async Task GenerateSourceAsync(string path, int durationSeconds = 2)
    {
        EnsureTools();
        // Tiny synthetic media via lavfi — no copyrighted binary committed.
        var args = new[]
        {
            "-f", "lavfi", "-i", $"testsrc=size=320x240:rate=25:duration={durationSeconds}",
            "-f", "lavfi", "-i", $"sine=frequency=440:duration={durationSeconds}",
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

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg for fixture.");
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0 || !File.Exists(path))
        {
            throw new InvalidOperationException($"Failed to generate lavfi fixture. exit={process.ExitCode} stderr={stderr}");
        }
    }

    private static async Task<IHost> StartHostAsync(TempWorkspace workspace)
    {
        EnsureTools();
        var dbPath = Path.Combine(workspace.Path, "jobs.db");
        var lockDir = Path.Combine(workspace.Path, "locks");
        Directory.CreateDirectory(lockDir);

        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(b => b.ClearProviders().SetMinimumLevel(LogLevel.Warning))
            .ConfigureServices(services =>
            {
                services.AddClipifyMediaJobs(new MediaJobHostingOptions
                {
                    DatabasePath = dbPath,
                    LockDirectory = lockDir,
                    LeaseOwner = "e2e-" + Guid.NewGuid().ToString("N")[..8],
                    MaxConcurrency = 1,
                    PollInterval = TimeSpan.FromMilliseconds(200),
                    ConfigureFFmpeg = o =>
                    {
                        o.FFmpegPath = _ffmpegPath;
                        o.FFprobePath = _ffprobePath;
                        o.CancelGracePeriod = TimeSpan.FromMilliseconds(300);
                    },
                });
            })
            .Build();

        await host.Services.MigrateClipifyDatabaseAsync();
        await host.StartAsync();
        return host;
    }

    private static async Task WaitSucceededAsync(IMediaJobService jobs, MediaJobId id)
    {
        await WaitForAsync(async () =>
        {
            var snap = await jobs.GetAsync(id);
            if (snap?.State is MediaJobState.Failed)
            {
                throw new InvalidOperationException($"Job failed: {snap.ErrorCode} {snap.ErrorMessage}");
            }

            return snap?.State == MediaJobState.Succeeded;
        }, TimeSpan.FromSeconds(30));
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Condition not met within timeout.");
    }

    private static string RunCapture(string exe, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {exe}");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return stdout + stderr;
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("clipify-e2e-").FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }
}
