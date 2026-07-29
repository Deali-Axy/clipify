using Clipify.Application.Abstractions;
using Clipify.Hosting;
using Clipify.Mcp.Security;
using Clipify.Mcp.Tools;
using Clipify.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Clipify.Mcp;

public static class McpApp
{
    public static Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default) =>
        RunAsync(args, runtimeOverrides: null, cancellationToken);

    /// <summary>
    /// Test/production entry. <paramref name="runtimeOverrides"/> replaces parsed options after argument parsing
    /// (or supplies options when <paramref name="args"/> are empty and overrides are set).
    /// </summary>
    public static async Task<int> RunAsync(
        string[] args,
        McpRuntimeOptions? runtimeOverrides,
        CancellationToken cancellationToken = default)
    {
        McpRuntimeOptions runtime;
        if (runtimeOverrides is not null && args.Length == 0)
        {
            runtime = runtimeOverrides;
        }
        else
        {
            try
            {
                if (!McpArgumentParser.TryParse(args, out var parsed, out var parseError))
                {
                    await Console.Error.WriteLineAsync(parseError).ConfigureAwait(false);
                    return 2;
                }

                runtime = runtimeOverrides is null
                    ? parsed!
                    : Merge(parsed!, runtimeOverrides);
            }
            catch (McpHelpRequestedException)
            {
                await Console.Error.WriteLineAsync(McpArgumentParser.HelpText).ConfigureAwait(false);
                return 0;
            }
        }

        return await RunWithRuntimeAsync(runtime, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<int> RunWithRuntimeAsync(
        McpRuntimeOptions runtime,
        CancellationToken cancellationToken = default)
    {
        AllowedRootPolicy roots;
        try
        {
            roots = AllowedRootPolicy.Create(runtime.AllowedRoots);
        }
        catch (ClipifyException ex)
        {
            await Console.Error.WriteLineAsync($"{ex.Code}: {ex.Message}").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(ex.Detail))
            {
                await Console.Error.WriteLineAsync(ex.Detail).ConfigureAwait(false);
            }

            return 2;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Failed to resolve allow-root policy: {ex.Message}")
                .ConfigureAwait(false);
            return 2;
        }

        var hostOptions = new ClipifyHostOptions
        {
            DataDirectory = runtime.DataDirectory,
            LeaseOwner = runtime.LeaseOwner ?? $"clipify-mcp-{Environment.ProcessId}-{Guid.NewGuid():N}",
            // Clear default console providers so nothing writes MCP protocol on stdout.
            SuppressConsoleLogging = true,
            EnableFileLogging = runtime.EnableFileLogging,
            PollInterval = runtime.PollInterval ?? TimeSpan.FromSeconds(2),
            ConfigureFFmpeg = runtime.ConfigureFFmpeg,
        };

        var paths = hostOptions.ResolvePaths();
        if (!ClipifyDoctor.TryPrepareDataRoot(paths, out var dataCheck))
        {
            await Console.Error.WriteLineAsync(dataCheck.Message).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(dataCheck.Detail))
            {
                await Console.Error.WriteLineAsync(dataCheck.Detail).ConfigureAwait(false);
            }

            return 1;
        }

        IHost? host = null;
        try
        {
            host = BuildHost(hostOptions, runtime, roots);
            await host.Services.MigrateClipifyDatabaseAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"clipify-mcp failed to start: {ex.Message}")
                .ConfigureAwait(false);
            return 1;
        }
        finally
        {
            host?.Dispose();
        }
    }

    /// <summary>
    /// Builds a migrated host with MCP tools registered, without starting the stdio transport.
    /// Used by tests that invoke tools in-process.
    /// </summary>
    public static async Task<IHost> StartHostAsync(
        McpRuntimeOptions runtime,
        CancellationToken cancellationToken = default)
    {
        var roots = AllowedRootPolicy.Create(runtime.AllowedRoots);
        var hostOptions = new ClipifyHostOptions
        {
            DataDirectory = runtime.DataDirectory,
            LeaseOwner = runtime.LeaseOwner ?? $"clipify-mcp-{Environment.ProcessId}-{Guid.NewGuid():N}",
            SuppressConsoleLogging = true,
            EnableFileLogging = runtime.EnableFileLogging,
            PollInterval = runtime.PollInterval ?? TimeSpan.FromMilliseconds(200),
            ConfigureFFmpeg = runtime.ConfigureFFmpeg,
        };

        var paths = hostOptions.ResolvePaths();
        if (!ClipifyDoctor.TryPrepareDataRoot(paths, out var dataCheck))
        {
            throw new ClipifyException(ClipifyErrorCode.Internal, dataCheck.Message, dataCheck.Detail);
        }

        var host = BuildHost(hostOptions, runtime, roots, includeStdioTransport: false);
        try
        {
            await host.Services.MigrateClipifyDatabaseAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            return host;
        }
        catch
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            host.Dispose();
            throw;
        }
    }

    private static IHost BuildHost(
        ClipifyHostOptions hostOptions,
        McpRuntimeOptions runtime,
        AllowedRootPolicy roots,
        bool includeStdioTransport = true)
    {
        var builder = ClipifyHostFactory.CreateBuilder(hostOptions);
        builder.ConfigureLogging(logging =>
        {
            // stdout is MCP JSON-RPC only; diagnostics go to stderr.
            logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });
        });
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(runtime);
            services.AddSingleton(roots);
            services.AddSingleton<ClipifyMcpTools>();
            var mcp = services.AddMcpServer().WithTools<ClipifyMcpTools>();
            if (includeStdioTransport)
            {
                mcp.WithStdioServerTransport();
            }
        });

        return builder.Build();
    }

    private static McpRuntimeOptions Merge(McpRuntimeOptions parsed, McpRuntimeOptions overrides) =>
        new()
        {
            DataDirectory = overrides.DataDirectory ?? parsed.DataDirectory,
            AllowedRoots = overrides.AllowedRoots.Count > 0 ? overrides.AllowedRoots : parsed.AllowedRoots,
            ConfigureFFmpeg = overrides.ConfigureFFmpeg ?? parsed.ConfigureFFmpeg,
            PollInterval = overrides.PollInterval ?? parsed.PollInterval,
            EnableFileLogging = overrides.EnableFileLogging,
            LeaseOwner = overrides.LeaseOwner ?? parsed.LeaseOwner,
        };
}
