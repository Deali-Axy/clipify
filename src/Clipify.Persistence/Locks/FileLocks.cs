using Clipify.Application.Jobs;
using Clipify.Domain.Jobs;

namespace Clipify.Persistence.Locks;

/// <summary>
/// Cross-process exclusive file lock proving a worker still owns a running job.
/// Separate from <see cref="FileMigrationLock"/>.
/// </summary>
public sealed class FileJobLock : IJobLock
{
    private readonly string _directory;

    public FileJobLock(string lockDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockDirectory);
        _directory = lockDirectory;
        Directory.CreateDirectory(_directory);
    }

    public ValueTask<IAsyncDisposable?> TryAcquireAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(jobId);
        try
        {
            var stream = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);

            return ValueTask.FromResult<IAsyncDisposable?>(new FileLockHandle(stream));
        }
        catch (IOException)
        {
            return ValueTask.FromResult<IAsyncDisposable?>(null);
        }
        catch (UnauthorizedAccessException)
        {
            return ValueTask.FromResult<IAsyncDisposable?>(null);
        }
    }

    public bool IsHeld(MediaJobId jobId, string? leaseOwner)
    {
        var path = GetPath(jobId);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private string GetPath(MediaJobId jobId) =>
        Path.Combine(_directory, $"job-{jobId.Value}.lock");

    private sealed class FileLockHandle(FileStream stream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            stream.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// Cross-process lock for EF Core MigrateAsync. Must not share paths with job locks.
/// </summary>
public sealed class FileMigrationLock
{
    private readonly string _path;

    public FileMigrationLock(string lockFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockFilePath);
        _path = lockFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(
                    _path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1);
                return new FileLockHandle(stream);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class FileLockHandle(FileStream stream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            stream.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
