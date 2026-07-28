namespace Clipify.Hosting;

/// <summary>
/// Cross-platform application data layout for SQLite, locks, and logs.
/// Tests may override the root via <see cref="ClipifyHostOptions.DataDirectory"/>.
/// </summary>
public sealed class ClipifyAppPaths
{
    public const string DataDirectoryEnvironmentVariable = "CLIPIFY_DATA_DIR";

    public string Root { get; }
    public string DatabasePath { get; }
    public string LockDirectory { get; }
    public string LogDirectory { get; }
    public string MigrationLockPath { get; }

    public ClipifyAppPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
        DatabasePath = Path.Combine(Root, "jobs.db");
        LockDirectory = Path.Combine(Root, "locks");
        LogDirectory = Path.Combine(Root, "logs");
        MigrationLockPath = Path.Combine(LockDirectory, "migrate.lock");
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LockDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    /// <summary>
    /// Resolves the default user data directory for the current OS.
    /// </summary>
    public static string ResolveDefaultRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return Path.GetFullPath(fromEnv);
        }

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Clipify");
        }

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "Clipify");
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return Path.Combine(Path.GetFullPath(xdg), "clipify");
        }

        var linuxHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(linuxHome, ".local", "share", "clipify");
    }

    public static ClipifyAppPaths CreateDefault() => new(ResolveDefaultRoot());

    public static ClipifyAppPaths Create(string? dataDirectoryOverride) =>
        new(string.IsNullOrWhiteSpace(dataDirectoryOverride)
            ? ResolveDefaultRoot()
            : Path.GetFullPath(dataDirectoryOverride));
}
