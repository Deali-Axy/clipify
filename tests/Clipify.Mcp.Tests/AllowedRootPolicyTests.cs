using Clipify.Application.Abstractions;
using Clipify.Mcp.Security;

namespace Clipify.Mcp.Tests;

public class AllowedRootPolicyTests
{
    [Fact]
    public void Allows_input_under_root()
    {
        using var root = new TempRoot();
        var file = Path.Combine(root.Path, "a.mp4");
        File.WriteAllText(file, "x");

        var policy = AllowedRootPolicy.Create([root.Path]);
        Assert.True(policy.TryResolve(file, PathAccessKind.InputFile, out var result, out var error));
        Assert.Null(error);
        Assert.Equal(Path.GetFullPath(file), result!.CanonicalPath);
    }

    [Fact]
    public void Rejects_path_outside_root()
    {
        using var root = new TempRoot();
        using var other = new TempRoot();
        var file = Path.Combine(other.Path, "secret.mp4");
        File.WriteAllText(file, "x");

        var policy = AllowedRootPolicy.Create([root.Path]);
        Assert.False(policy.TryResolve(file, PathAccessKind.InputFile, out _, out var error));
        Assert.Equal(ClipifyErrorCode.Validation, error!.Code);
        Assert.DoesNotContain(other.Path, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("PathDenied", error.Detail);
    }

    [Fact]
    public void Rejects_prefix_collision()
    {
        using var parent = new TempRoot();
        var allowed = Directory.CreateDirectory(Path.Combine(parent.Path, "allowed")).FullName;
        var evil = Directory.CreateDirectory(Path.Combine(parent.Path, "allowed-evil")).FullName;
        var file = Path.Combine(evil, "x.mp4");
        File.WriteAllText(file, "x");

        var policy = AllowedRootPolicy.Create([allowed]);
        Assert.False(policy.TryResolve(file, PathAccessKind.InputFile, out _, out var error));
        Assert.Equal(ClipifyErrorCode.Validation, error!.Code);
    }

    [Fact]
    public void Rejects_dotdot_escape()
    {
        using var parent = new TempRoot();
        var allowed = Directory.CreateDirectory(Path.Combine(parent.Path, "allowed")).FullName;
        var outside = Path.Combine(parent.Path, "outside.mp4");
        File.WriteAllText(outside, "x");

        var sneaky = Path.Combine(allowed, "..", "outside.mp4");
        var policy = AllowedRootPolicy.Create([allowed]);
        Assert.False(policy.TryResolve(sneaky, PathAccessKind.InputFile, out _, out var error));
        Assert.Equal(ClipifyErrorCode.Validation, error!.Code);
    }

    [Fact]
    public void Output_requires_existing_parent_directory()
    {
        using var root = new TempRoot();
        var missingParent = Path.Combine(root.Path, "missing", "out.mp4");
        var policy = AllowedRootPolicy.Create([root.Path]);
        Assert.False(policy.TryResolve(missingParent, PathAccessKind.OutputFile, out _, out var error));
        Assert.Equal(ClipifyErrorCode.Validation, error!.Code);
        Assert.Contains("parent directory", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Output_allows_new_file_when_parent_exists()
    {
        using var root = new TempRoot();
        var output = Path.Combine(root.Path, "out.mp4");
        var policy = AllowedRootPolicy.Create([root.Path]);
        Assert.True(policy.TryResolve(output, PathAccessKind.OutputFile, out var result, out var error));
        Assert.Null(error);
        Assert.Equal(Path.GetFullPath(output), result!.CanonicalPath);
    }

    [Fact]
    public void Rejects_url_inputs()
    {
        using var root = new TempRoot();
        var policy = AllowedRootPolicy.Create([root.Path]);
        Assert.False(policy.TryResolve("https://example.com/a.mp4", PathAccessKind.InputFile, out _, out var error));
        Assert.Equal(ClipifyErrorCode.Validation, error!.Code);
    }

    [Fact]
    public void Rejects_empty_allow_root_entries()
    {
        var ex = Assert.Throws<ClipifyException>(() => AllowedRootPolicy.Create(["", " "]));
        Assert.Equal(ClipifyErrorCode.Validation, ex.Code);
    }

    [Fact]
    public void Case_sensitivity_matches_platform()
    {
        using var root = new TempRoot();
        var file = Path.Combine(root.Path, "Clip.MP4");
        File.WriteAllText(file, "x");

        var policy = AllowedRootPolicy.Create([root.Path]);
        if (OperatingSystem.IsWindows())
        {
            var alt = file.ToLowerInvariant();
            Assert.True(policy.TryResolve(alt, PathAccessKind.InputFile, out _, out _));
        }
        else
        {
            // On case-sensitive volumes, a differently-cased path may not exist as a file.
            // Still ensure the configured root itself is retained with exact casing.
            Assert.Contains(Path.GetFullPath(root.Path).TrimEnd(Path.DirectorySeparatorChar), policy.Roots);
        }
    }

    [Fact]
    public void Junction_escape_is_rejected_when_supported()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var parent = new TempRoot();
        var allowed = Directory.CreateDirectory(Path.Combine(parent.Path, "allowed")).FullName;
        var outsideDir = Directory.CreateDirectory(Path.Combine(parent.Path, "outside")).FullName;
        var outsideFile = Path.Combine(outsideDir, "leak.mp4");
        File.WriteAllText(outsideFile, "secret");

        var junction = Path.Combine(allowed, "link");
        try
        {
            Directory.CreateSymbolicLink(junction, outsideDir);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // Creating junctions/symlinks may require Developer Mode / elevation.
            return;
        }

        var viaLink = Path.Combine(junction, "leak.mp4");
        var policy = AllowedRootPolicy.Create([allowed]);
        Assert.False(policy.TryResolve(viaLink, PathAccessKind.InputFile, out _, out var error));
        Assert.Equal(ClipifyErrorCode.Validation, error!.Code);
    }

    private sealed class TempRoot : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("clipify-root-").FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
