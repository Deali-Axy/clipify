using Clipify.Hosting;

namespace Clipify.Cli.Tests;

public class HostingAppPathsTests
{
    [Fact]
    public void Explicit_data_directory_overrides_default()
    {
        using var temp = new TempDataDirectory();
        var paths = ClipifyAppPaths.Create(temp.Path);
        Assert.Equal(Path.GetFullPath(temp.Path), paths.Root);
        Assert.Equal(Path.Combine(paths.Root, "jobs.db"), paths.DatabasePath);
        Assert.Equal(Path.Combine(paths.Root, "locks"), paths.LockDirectory);
        Assert.Equal(Path.Combine(paths.Root, "logs"), paths.LogDirectory);
    }

    [Fact]
    public void EnsureCreated_makes_directories()
    {
        using var temp = new TempDataDirectory();
        var root = Path.Combine(temp.Path, "nested", "clipify-data");
        var paths = new ClipifyAppPaths(root);
        paths.EnsureCreated();
        Assert.True(Directory.Exists(paths.Root));
        Assert.True(Directory.Exists(paths.LockDirectory));
        Assert.True(Directory.Exists(paths.LogDirectory));
    }

    [Fact]
    public void Environment_variable_overrides_default_root()
    {
        using var temp = new TempDataDirectory();
        var previous = Environment.GetEnvironmentVariable(ClipifyAppPaths.DataDirectoryEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ClipifyAppPaths.DataDirectoryEnvironmentVariable, temp.Path);
            var root = ClipifyAppPaths.ResolveDefaultRoot();
            Assert.Equal(Path.GetFullPath(temp.Path), root);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ClipifyAppPaths.DataDirectoryEnvironmentVariable, previous);
        }
    }
}
