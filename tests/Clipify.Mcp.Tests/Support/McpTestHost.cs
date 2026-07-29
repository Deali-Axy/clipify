using System.Diagnostics;
using System.Text.Json;
using Clipify.FFmpeg;
using Clipify.Mcp.Dto;
using Microsoft.Extensions.Hosting;

namespace Clipify.Mcp.Tests;

public sealed class TempWorkspace : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("clipify-mcp-").FullName;

    public string DataDirectory => System.IO.Path.Combine(Path, "data");

    public string MediaRoot => System.IO.Path.Combine(Path, "media");

    public TempWorkspace()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(MediaRoot);
    }

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

public static class McpTestHost
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
                    "FFmpeg/ffprobe required for Clipify.Mcp.Tests media scenarios.");
            }

            var root = Directory.CreateTempSubdirectory("clipify-mcp-shared-").FullName;
            _sourceVideo = System.IO.Path.Combine(root, "source.mp4");
            await GenerateSourceAsync(_sourceVideo);
        }
        finally
        {
            InitLock.Release();
        }
    }

    public static string SourceVideo =>
        _sourceVideo ?? throw new InvalidOperationException("Call EnsureMediaFixtureAsync first.");

    public static McpRuntimeOptions CreateRuntime(TempWorkspace workspace) =>
        new()
        {
            DataDirectory = workspace.DataDirectory,
            AllowedRoots = [workspace.MediaRoot],
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

    public static async Task<StartedMcpHost> StartAsync(TempWorkspace workspace)
    {
        await EnsureMediaFixtureAsync();
        var host = await McpApp.StartHostAsync(CreateRuntime(workspace));
        return new StartedMcpHost(host);
    }

    public static string CopySourceInto(TempWorkspace workspace, string fileName = "input.mp4")
    {
        var dest = System.IO.Path.Combine(workspace.MediaRoot, fileName);
        File.Copy(SourceVideo, dest, overwrite: true);
        return dest;
    }

    public static McpToolResponse ParseResponse(string json) =>
        JsonSerializer.Deserialize<McpToolResponse>(json, McpJson.Options)
        ?? throw new InvalidOperationException("Tool response deserialized to null.");

    private static async Task GenerateSourceAsync(string outputPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath!,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList =
            {
                "-y",
                "-f", "lavfi",
                "-i", "testsrc=size=320x240:rate=30",
                "-f", "lavfi",
                "-i", "sine=frequency=440:sample_rate=44100",
                "-t", "2",
                "-c:v", "libx264",
                "-pix_fmt", "yuv420p",
                "-c:a", "aac",
                outputPath,
            },
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg.");
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg fixture failed: {stderr}");
        }
    }
}

public sealed class StartedMcpHost : IAsyncDisposable
{
    private readonly IHost _host;

    public StartedMcpHost(IHost host) => _host = host;

    public IServiceProvider Services => _host.Services;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch
        {
            // best-effort shutdown for tests
        }

        _host.Dispose();
    }
}
