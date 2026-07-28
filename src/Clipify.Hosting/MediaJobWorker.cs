using Clipify.Application.Jobs;
using Clipify.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clipify.Hosting;

/// <summary>
/// Thin hosted service that runs the Application media job execution loop.
/// </summary>
public sealed class MediaJobWorker : BackgroundService
{
    private readonly MediaJobExecutor _executor;
    private readonly ILogger<MediaJobWorker> _logger;

    public MediaJobWorker(MediaJobExecutor executor, ILogger<MediaJobWorker> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MediaJobWorker starting for lease owner loop.");
        try
        {
            await _executor.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _logger.LogInformation("MediaJobWorker stopped.");
        }
    }
}

public sealed class MediaJobHostingOptions
{
    public required string DatabasePath { get; init; }
    public required string LockDirectory { get; init; }
    public required string LeaseOwner { get; init; }
    public int MaxConcurrency { get; init; } = 1;
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);
    public int QueueCapacity { get; init; } = 256;
}

public static class MediaJobHostingExtensions
{
    /// <summary>
    /// Registers Application job services, EF SQLite persistence, and the media job worker.
    /// Callers must also call <see cref="Persistence.PersistenceServiceCollectionExtensions.MigrateClipifyDatabaseAsync"/> before starting.
    /// </summary>
    public static IServiceCollection AddClipifyMediaJobs(
        this IServiceCollection services,
        MediaJobHostingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LeaseOwner);
        if (options.MaxConcurrency is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxConcurrency), "MaxConcurrency must be 1 or 2.");
        }

        services.AddClipifyPersistence(new Persistence.ClipifyPersistenceOptions
        {
            DatabasePath = options.DatabasePath,
            LockDirectory = options.LockDirectory,
        });

        services.AddSingleton(new MediaJobWorkerOptions
        {
            LeaseOwner = options.LeaseOwner,
            MaxConcurrency = options.MaxConcurrency,
            LeaseDuration = options.LeaseDuration,
            HeartbeatInterval = options.HeartbeatInterval,
            PollInterval = options.PollInterval,
        });

        services.AddSingleton<IMediaJobQueue>(_ => new ChannelMediaJobQueue(options.QueueCapacity));
        services.AddSingleton<IMediaJobChangePublisher, ChannelMediaJobChangePublisher>();
        services.AddSingleton<IJobCancellationRegistry, InMemoryJobCancellationRegistry>();
        services.AddSingleton<IMediaJobHandler<Domain.Jobs.FakeDelayJobDefinition>, FakeDelayJobHandler>();
        services.AddSingleton<IMediaJobHandlerDispatcher, MediaJobHandlerDispatcher>();
        services.AddSingleton<IMediaJobService, MediaJobService>();
        services.AddSingleton<MediaJobExecutor>();
        services.AddHostedService<MediaJobWorker>();
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
