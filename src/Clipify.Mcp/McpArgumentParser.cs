using Clipify.Application.Abstractions;

namespace Clipify.Mcp;

/// <summary>
/// Parses MCP process arguments. Does not touch stdout — callers write diagnostics to stderr.
/// </summary>
public static class McpArgumentParser
{
    public const string AllowedRootsEnvironmentVariable = "CLIPIFY_ALLOWED_ROOTS";

    public static bool TryParse(
        string[] args,
        out McpRuntimeOptions? options,
        out string? errorMessage)
    {
        options = null;
        errorMessage = null;

        string? dataDirectory = null;
        var allowRoots = new List<string>();
        var enableFileLogging = true;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--data-dir":
                    if (!TryReadValue(args, ref i, out var dataDir))
                    {
                        errorMessage = "--data-dir requires a path value.";
                        return false;
                    }

                    dataDirectory = dataDir;
                    break;

                case "--allow-root":
                    if (!TryReadValue(args, ref i, out var root))
                    {
                        errorMessage = "--allow-root requires a path value.";
                        return false;
                    }

                    allowRoots.Add(root);
                    break;

                case "--no-file-log":
                    enableFileLogging = false;
                    break;

                case "--help":
                case "-h":
                    errorMessage = null;
                    options = null;
                    // Special: help requested.
                    throw new McpHelpRequestedException();

                default:
                    errorMessage = $"Unknown argument: {arg}";
                    return false;
            }
        }

        foreach (var envRoot in ReadEnvironmentRoots())
        {
            allowRoots.Add(envRoot);
        }

        if (allowRoots.Count == 0)
        {
            allowRoots.Add(Environment.CurrentDirectory);
        }

        // Detect empty entries early (before directory existence checks).
        if (allowRoots.Any(string.IsNullOrWhiteSpace))
        {
            errorMessage = "Allowed root entries must not be empty.";
            return false;
        }

        options = new McpRuntimeOptions
        {
            DataDirectory = dataDirectory,
            AllowedRoots = allowRoots,
            EnableFileLogging = enableFileLogging,
        };
        return true;
    }

    public static string HelpText =>
        """
        clipify-mcp — local MCP stdio server for Clipify media jobs.

        Usage:
          clipify-mcp [--data-dir <path>] [--allow-root <path>]... [--no-file-log]

        Options:
          --data-dir <path>     Override Clipify data directory (jobs.db, logs, locks).
          --allow-root <path>   Allow media input/output under this directory (repeatable).
          --no-file-log         Disable file logging under the data directory.
          --help, -h            Show this help.

        Environment:
          CLIPIFY_DATA_DIR          Default data directory.
          CLIPIFY_ALLOWED_ROOTS     Extra allow-root paths separated by the OS path separator.

        Notes:
          - stdout is reserved for MCP JSON-RPC. Logs go to stderr / data-dir logs.
          - Default allow-root is the process current working directory when none are configured.
        """;

    private static bool TryReadValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
        {
            value = "";
            return false;
        }

        index++;
        value = args[index];
        return true;
    }

    private static IEnumerable<string> ReadEnvironmentRoots()
    {
        var raw = Environment.GetEnvironmentVariable(AllowedRootsEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            yield break;
        }

        foreach (var part in raw.Split(Path.PathSeparator, StringSplitOptions.None))
        {
            yield return part;
        }
    }
}

public sealed class McpHelpRequestedException : Exception;
