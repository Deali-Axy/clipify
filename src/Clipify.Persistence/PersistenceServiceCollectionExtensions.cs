using Clipify.Application.Jobs;
using Clipify.Persistence.Jobs;
using Clipify.Persistence.Locks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clipify.Persistence;

public sealed class ClipifyPersistenceOptions
{
    public required string DatabasePath { get; init; }
    public required string LockDirectory { get; init; }
    public string? MigrationLockPath { get; init; }
}

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddClipifyPersistence(
        this IServiceCollection services,
        ClipifyPersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LockDirectory);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.DatabasePath))!);
        Directory.CreateDirectory(options.LockDirectory);

        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
            Cache = Microsoft.Data.Sqlite.SqliteCacheMode.Shared,
            DefaultTimeout = 5,
        }.ToString();

        services.AddSingleton(options);
        services.AddDbContextFactory<ClipifyDbContext>(db =>
            db.UseSqlite(connectionString));

        services.AddSingleton<IMediaJobStore, EfMediaJobStore>();
        services.AddSingleton<IJobLock>(_ => new FileJobLock(options.LockDirectory));
        services.AddSingleton(_ => new FileMigrationLock(
            options.MigrationLockPath
            ?? Path.Combine(options.LockDirectory, "migrate.lock")));

        return services;
    }

    public static async Task MigrateClipifyDatabaseAsync(
        this IServiceProvider services,
        TimeSpan? lockTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var migrationLock = services.GetRequiredService<FileMigrationLock>();
        await using var handle = await migrationLock
            .AcquireAsync(lockTimeout ?? TimeSpan.FromSeconds(60), cancellationToken)
            .ConfigureAwait(false);

        var factory = services.GetRequiredService<IDbContextFactory<ClipifyDbContext>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
