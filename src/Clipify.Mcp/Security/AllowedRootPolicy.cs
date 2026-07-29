using Clipify.Application.Abstractions;

namespace Clipify.Mcp.Security;

public enum PathAccessKind
{
    InputFile,
    OutputFile,
}

public sealed class PathResolutionResult
{
    public required string CanonicalPath { get; init; }

    public required string AllowedRoot { get; init; }
}

/// <summary>
/// Enforces allow-root boundaries with symlink/junction resolution and prefix-collision safety.
/// </summary>
public sealed class AllowedRootPolicy
{
    private readonly IReadOnlyList<string> _roots;
    private readonly StringComparison _comparison;

    private AllowedRootPolicy(IReadOnlyList<string> roots)
    {
        _roots = roots;
        _comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public IReadOnlyList<string> Roots => _roots;

    public static AllowedRootPolicy Create(IEnumerable<string> rootCandidates)
    {
        ArgumentNullException.ThrowIfNull(rootCandidates);

        var normalized = new List<string>();
        var seen = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var candidate in rootCandidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                throw new ClipifyException(
                    ClipifyErrorCode.Validation,
                    "Allowed root entry is empty.");
            }

            string full;
            try
            {
                full = Path.GetFullPath(candidate.Trim());
            }
            catch (Exception ex)
            {
                throw new ClipifyException(
                    ClipifyErrorCode.Validation,
                    "Allowed root path could not be resolved.",
                    ex.Message,
                    ex);
            }

            if (!Directory.Exists(full))
            {
                throw new ClipifyException(
                    ClipifyErrorCode.Validation,
                    "Allowed root does not exist or is not a directory.");
            }

            // Resolve directory junctions/symlinks at startup so roots are stable.
            var resolved = ResolveDirectoryCanonical(full);
            if (!Directory.Exists(resolved))
            {
                throw new ClipifyException(
                    ClipifyErrorCode.Validation,
                    "Allowed root does not exist or is not a directory after link resolution.");
            }

            var trimmed = TrimTrailingSeparators(resolved);
            if (seen.Add(trimmed))
            {
                normalized.Add(trimmed);
            }
        }

        if (normalized.Count == 0)
        {
            throw new ClipifyException(
                ClipifyErrorCode.Validation,
                "At least one allowed root is required.");
        }

        return new AllowedRootPolicy(normalized);
    }

    public bool TryResolve(
        string? path,
        PathAccessKind kind,
        out PathResolutionResult? result,
        out ClipifyError? error)
    {
        result = null;
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = ClipifyError.Validation($"{kind} path is required.");
            return false;
        }

        if (LooksLikeUrl(path))
        {
            error = ClipifyError.Validation("Network URLs are not allowed.", SafeSummary(path));
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path.Trim());
        }
        catch (Exception)
        {
            error = ClipifyError.Validation("Path could not be resolved.", SafeSummary(path));
            return false;
        }

        try
        {
            return kind switch
            {
                PathAccessKind.InputFile => TryResolveInput(full, out result, out error),
                PathAccessKind.OutputFile => TryResolveOutput(full, out result, out error),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };
        }
        catch (ClipifyException ex)
        {
            error = ex.ToError();
            return false;
        }
        catch (Exception)
        {
            error = ClipifyError.Validation("Path access was denied by the allow-root policy.");
            return false;
        }
    }

    private bool TryResolveInput(string fullPath, out PathResolutionResult? result, out ClipifyError? error)
    {
        result = null;
        error = null;

        if (!File.Exists(fullPath))
        {
            error = ClipifyError.NotFound("Input file was not found.");
            return false;
        }

        var resolvedFile = ResolveFileCanonical(fullPath);
        if (!File.Exists(resolvedFile))
        {
            error = ClipifyError.NotFound("Input file was not found after link resolution.");
            return false;
        }

        if (!TryFindRoot(resolvedFile, out var root))
        {
            error = PathDenied();
            return false;
        }

        result = new PathResolutionResult { CanonicalPath = resolvedFile, AllowedRoot = root! };
        return true;
    }

    private bool TryResolveOutput(string fullPath, out PathResolutionResult? result, out ClipifyError? error)
    {
        result = null;
        error = null;

        var parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(parent))
        {
            error = ClipifyError.Validation("Output path must include a parent directory.");
            return false;
        }

        if (!Directory.Exists(parent))
        {
            error = ClipifyError.Validation(
                "Output parent directory must already exist. MCP does not create directories.");
            return false;
        }

        var resolvedParent = ResolveDirectoryCanonical(parent);
        if (!Directory.Exists(resolvedParent))
        {
            error = ClipifyError.Validation(
                "Output parent directory must already exist after link resolution.");
            return false;
        }

        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            error = ClipifyError.Validation("Output path must include a file name.");
            return false;
        }

        // Recombine after resolving the real parent so junctions cannot escape.
        var combined = Path.GetFullPath(Path.Combine(resolvedParent, fileName));
        if (!TryFindRoot(combined, out var root) || !TryFindRoot(resolvedParent, out _))
        {
            error = PathDenied();
            return false;
        }

        // If the output already exists as a symlink, resolve and re-check.
        if (File.Exists(combined) || Directory.Exists(combined))
        {
            var existing = File.Exists(combined)
                ? ResolveFileCanonical(combined)
                : ResolveDirectoryCanonical(combined);
            if (!TryFindRoot(existing, out root))
            {
                error = PathDenied();
                return false;
            }

            combined = existing;
        }

        result = new PathResolutionResult { CanonicalPath = combined, AllowedRoot = root! };
        return true;
    }

    private bool TryFindRoot(string canonicalPath, out string? root)
    {
        root = null;
        var path = TrimTrailingSeparators(canonicalPath);
        foreach (var candidate in _roots)
        {
            if (IsUnderRoot(path, candidate))
            {
                root = candidate;
                return true;
            }
        }

        return false;
    }

    private bool IsUnderRoot(string path, string root)
    {
        if (path.Equals(root, _comparison))
        {
            return true;
        }

        // Reject prefix collisions such as /allowed vs /allowed-evil.
        if (!path.StartsWith(root, _comparison))
        {
            return false;
        }

        if (path.Length == root.Length)
        {
            return true;
        }

        var next = path[root.Length];
        return next == Path.DirectorySeparatorChar || next == Path.AltDirectorySeparatorChar;
    }

    private static ClipifyError PathDenied() =>
        ClipifyError.Validation(
            "Path is outside the configured allow-root directories.",
            "PathDenied");

    private static string SafeSummary(string path)
    {
        var name = Path.GetFileName(path.Trim());
        return string.IsNullOrEmpty(name) ? "(path)" : name;
    }

    private static bool LooksLikeUrl(string path)
    {
        return path.Contains("://", StringComparison.Ordinal)
            || path.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("https:", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("ftp:", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveFileCanonical(string path)
    {
        try
        {
            var target = File.ResolveLinkTarget(path, returnFinalTarget: true);
            if (target is not null)
            {
                return Path.GetFullPath(target.FullName);
            }
        }
        catch (IOException)
        {
            // Fall through to GetFullPath for non-link or unsupported link types.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return Path.GetFullPath(path);
    }

    private static string ResolveDirectoryCanonical(string path)
    {
        try
        {
            var target = Directory.ResolveLinkTarget(path, returnFinalTarget: true);
            if (target is not null)
            {
                return TrimTrailingSeparators(Path.GetFullPath(target.FullName));
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        // Walk parents and recombine to collapse intermediate junctions when ResolveLinkTarget
        // is unavailable for a segment.
        var full = Path.GetFullPath(path);
        var info = new DirectoryInfo(full);
        if (info.Exists && info.LinkTarget is not null)
        {
            try
            {
                var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
                if (resolved is not null)
                {
                    return TrimTrailingSeparators(Path.GetFullPath(resolved.FullName));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return TrimTrailingSeparators(full);
    }

    private static string TrimTrailingSeparators(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        // Keep root paths like "C:\" or "/".
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0
            || (OperatingSystem.IsWindows() && trimmed.Length == 2 && trimmed[1] == ':'))
        {
            return path.Length >= 3 ? path[..3] : path;
        }

        return trimmed;
    }
}
