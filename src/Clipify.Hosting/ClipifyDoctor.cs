using System.Runtime.InteropServices;
using Clipify.Application.Abstractions;
using Clipify.Application.Media;
using Clipify.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clipify.Hosting;

public interface IClipifyDoctor
{
    Task<ClipifyDoctorReport> RunAsync(CancellationToken cancellationToken = default);
}

public sealed class ClipifyDoctor : IClipifyDoctor
{
    private readonly ClipifyAppPaths _paths;
    private readonly IFFmpegLocator _locator;
    private readonly IFFmpegVersionProbe _versionProbe;
    private readonly IServiceProvider _services;

    public ClipifyDoctor(
        ClipifyAppPaths paths,
        IFFmpegLocator locator,
        IFFmpegVersionProbe versionProbe,
        IServiceProvider services)
    {
        _paths = paths;
        _locator = locator;
        _versionProbe = versionProbe;
        _services = services;
    }

    public async Task<ClipifyDoctorReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<ClipifyDoctorCheck>();

        checks.Add(CheckDirectory("data_directory", _paths.Root));
        checks.Add(CheckDirectory("lock_directory", _paths.LockDirectory));
        checks.Add(CheckDirectory("log_directory", _paths.LogDirectory));

        checks.Add(await CheckFFmpegAsync(cancellationToken).ConfigureAwait(false));
        checks.Add(await CheckFFprobeAsync(cancellationToken).ConfigureAwait(false));
        checks.Add(await CheckSqliteAsync(cancellationToken).ConfigureAwait(false));

        return CreateReport(_paths, checks);
    }

    /// <summary>
    /// Validates that <paramref name="paths"/>.Root can be used as an application data directory
    /// before Host/DI construction. Returns false when the root is a file or cannot be created.
    /// </summary>
    public static bool TryPrepareDataRoot(ClipifyAppPaths paths, out ClipifyDoctorCheck dataDirectoryCheck)
    {
        ArgumentNullException.ThrowIfNull(paths);

        try
        {
            if (File.Exists(paths.Root) && !Directory.Exists(paths.Root))
            {
                dataDirectoryCheck = new ClipifyDoctorCheck(
                    "data_directory",
                    false,
                    $"Path is a file, not a directory: {paths.Root}",
                    paths.Root);
                return false;
            }

            paths.EnsureCreated();
            var writable = IsDirectoryWritable(paths.Root);
            dataDirectoryCheck = new ClipifyDoctorCheck(
                "data_directory",
                writable,
                writable ? $"Directory ready: {paths.Root}" : $"Directory not writable: {paths.Root}",
                paths.Root);
            return writable;
        }
        catch (Exception ex)
        {
            dataDirectoryCheck = new ClipifyDoctorCheck(
                "data_directory",
                false,
                $"Directory check failed: {ex.Message}",
                paths.Root);
            return false;
        }
    }

    /// <summary>
    /// Builds a doctor report when Host bootstrap cannot run (invalid data root).
    /// </summary>
    public static ClipifyDoctorReport CreateBootstrapFailureReport(
        ClipifyAppPaths paths,
        ClipifyDoctorCheck dataDirectoryCheck)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(dataDirectoryCheck);

        var checks = new List<ClipifyDoctorCheck>
        {
            dataDirectoryCheck,
            new("lock_directory", false, "Skipped: data directory is not usable.", paths.LockDirectory),
            new("log_directory", false, "Skipped: data directory is not usable.", paths.LogDirectory),
            new("ffmpeg", false, "Skipped: data directory is not usable.", null),
            new("ffprobe", false, "Skipped: data directory is not usable.", null),
            new("sqlite", false, "Skipped: data directory is not usable.", paths.DatabasePath),
        };

        return CreateReport(paths, checks);
    }

    public static ClipifyDoctorPlatform CreatePlatformInfo() =>
        new(
            Os: GetOsName(),
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            FrameworkDescription: RuntimeInformation.FrameworkDescription,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeIdentifier: RuntimeInformation.RuntimeIdentifier);

    private static ClipifyDoctorReport CreateReport(ClipifyAppPaths paths, IReadOnlyList<ClipifyDoctorCheck> checks) =>
        new(
            Ok: checks.All(c => c.Ok),
            Paths: new ClipifyDoctorPaths(
                Root: paths.Root,
                DatabasePath: paths.DatabasePath,
                LockDirectory: paths.LockDirectory,
                LogDirectory: paths.LogDirectory),
            Platform: CreatePlatformInfo(),
            Checks: checks);

    private static ClipifyDoctorCheck CheckDirectory(string name, string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var writable = IsDirectoryWritable(path);
            return new ClipifyDoctorCheck(
                Name: name,
                Ok: writable,
                Message: writable ? $"Directory ready: {path}" : $"Directory not writable: {path}",
                Detail: path);
        }
        catch (Exception ex)
        {
            return new ClipifyDoctorCheck(name, false, $"Directory check failed: {ex.Message}", path);
        }
    }

    private async Task<ClipifyDoctorCheck> CheckFFmpegAsync(CancellationToken cancellationToken)
    {
        try
        {
            var path = _locator.ResolveFFmpegPath();
            var version = await _versionProbe.ProbeFFmpegAsync(cancellationToken).ConfigureAwait(false);
            return new ClipifyDoctorCheck(
                "ffmpeg",
                true,
                version.VersionLine,
                path);
        }
        catch (ClipifyException ex)
        {
            return new ClipifyDoctorCheck("ffmpeg", false, ex.Message, ex.Detail);
        }
        catch (Exception ex)
        {
            return new ClipifyDoctorCheck("ffmpeg", false, ex.Message, null);
        }
    }

    private async Task<ClipifyDoctorCheck> CheckFFprobeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var path = _locator.ResolveFFprobePath();
            var version = await _versionProbe.ProbeFFprobeAsync(cancellationToken).ConfigureAwait(false);
            return new ClipifyDoctorCheck(
                "ffprobe",
                true,
                version.VersionLine,
                path);
        }
        catch (ClipifyException ex)
        {
            return new ClipifyDoctorCheck("ffprobe", false, ex.Message, ex.Detail);
        }
        catch (Exception ex)
        {
            return new ClipifyDoctorCheck("ffprobe", false, ex.Message, null);
        }
    }

    private async Task<ClipifyDoctorCheck> CheckSqliteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _services.MigrateClipifyDatabaseAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var factory = _services.GetRequiredService<IDbContextFactory<ClipifyDbContext>>();
            await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var canConnect = await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
            if (!canConnect)
            {
                return new ClipifyDoctorCheck("sqlite", false, "Cannot connect to SQLite database.", _paths.DatabasePath);
            }

            await using var connection = db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT sqlite_version();";
            var version = (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            return new ClipifyDoctorCheck(
                "sqlite",
                true,
                $"SQLite {version} at {_paths.DatabasePath}",
                _paths.DatabasePath);
        }
        catch (SqliteException ex)
        {
            return new ClipifyDoctorCheck("sqlite", false, ex.Message, _paths.DatabasePath);
        }
        catch (Exception ex)
        {
            return new ClipifyDoctorCheck("sqlite", false, ex.Message, _paths.DatabasePath);
        }
    }

    private static bool IsDirectoryWritable(string path)
    {
        var probe = Path.Combine(path, $".clipify-write-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetOsName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macos";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        return "unknown";
    }
}

public sealed record ClipifyDoctorReport(
    bool Ok,
    ClipifyDoctorPaths Paths,
    ClipifyDoctorPlatform Platform,
    IReadOnlyList<ClipifyDoctorCheck> Checks);

public sealed record ClipifyDoctorPaths(
    string Root,
    string DatabasePath,
    string LockDirectory,
    string LogDirectory);

public sealed record ClipifyDoctorPlatform(
    string Os,
    string Architecture,
    string FrameworkDescription,
    string ProcessArchitecture,
    string RuntimeIdentifier);

public sealed record ClipifyDoctorCheck(
    string Name,
    bool Ok,
    string Message,
    string? Detail);
