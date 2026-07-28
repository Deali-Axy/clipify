using Clipify.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clipify.Hosting;

/// <summary>
/// Testable host factory. Startup order: resolve paths → build host → migrate → start worker.
/// </summary>
public static class ClipifyHostFactory
{
    public static IHostBuilder CreateBuilder(ClipifyHostOptions options, bool ensureDirectories = true)
    {
        ArgumentNullException.ThrowIfNull(options);

        var paths = options.ResolvePaths();
        if (ensureDirectories)
        {
            paths.EnsureCreated();
        }

        var hostingOptions = new MediaJobHostingOptions
        {
            DatabasePath = paths.DatabasePath,
            LockDirectory = paths.LockDirectory,
            LeaseOwner = options.ResolveLeaseOwner(),
            MaxConcurrency = options.MaxConcurrency,
            LeaseDuration = options.LeaseDuration,
            HeartbeatInterval = options.HeartbeatInterval,
            PollInterval = options.PollInterval,
            QueueCapacity = options.QueueCapacity,
            ConfigureFFmpeg = options.ConfigureFFmpeg,
        };

        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                if (options.SuppressConsoleLogging)
                {
                    logging.ClearProviders();
                }

                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton(options);
                services.AddSingleton(paths);
                services.AddSingleton<IClipifyDoctor, ClipifyDoctor>();
                services.AddClipifyMediaJobs(hostingOptions);

                if (options.EnableFileLogging)
                {
                    services.AddSingleton<ILoggerProvider>(_ =>
                        new SimpleFileLoggerProvider(Path.Combine(paths.LogDirectory, "clipify.log")));
                }
            });
    }

    public static IHost Build(ClipifyHostOptions options) => CreateBuilder(options).Build();

    /// <summary>
    /// Builds the host, acquires the migration lock, migrates, then starts hosted services (Worker).
    /// </summary>
    public static async Task<IHost> StartAsync(
        ClipifyHostOptions options,
        CancellationToken cancellationToken = default)
    {
        var host = Build(options);
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

    /// <summary>
    /// Builds DI without migrating or starting the Worker. Does not create directories — callers
    /// (doctor) must validate/prepare the data root first so path failures can be reported.
    /// </summary>
    public static IHost BuildForDiagnostics(ClipifyHostOptions options) =>
        CreateBuilder(options, ensureDirectories: false).Build();
}

/// <summary>
/// Minimal rolling-free file logger so JSON stdout is not polluted by console providers.
/// </summary>
internal sealed class SimpleFileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly object _gate = new();

    public SimpleFileLoggerProvider(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    }

    public ILogger CreateLogger(string categoryName) => new SimpleFileLogger(categoryName, _path, _gate);

    public void Dispose()
    {
    }

    private sealed class SimpleFileLogger : ILogger
    {
        private readonly string _category;
        private readonly string _path;
        private readonly object _gate;

        public SimpleFileLogger(string category, string path, object gate)
        {
            _category = category;
            _path = path;
            _gate = gate;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line =
                $"{DateTimeOffset.UtcNow:O} [{logLevel}] {_category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            lock (_gate)
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }
    }
}
