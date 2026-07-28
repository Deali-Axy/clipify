using Clipify.Domain.Jobs;

namespace Clipify.Application.Jobs;

/// <summary>
/// Writes post-commit progress and a single stable artifact with separated retries.
/// </summary>
public static class PostCommitFinalizer
{
    public static async Task FinalizeAsync(
        MediaJobExecutionContext context,
        MediaArtifact artifact,
        TimeSpan? totalDuration,
        TimeProvider timeProvider,
        int maxAttempts = 3,
        Func<Exception, string, int, int, Task>? onRetry = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        }

        await RetryAsync(
                "commit progress",
                maxAttempts,
                onRetry,
                async () =>
                {
                    await context.ReportProgressAsync(
                            MediaJobProgress.Create(
                                "commit",
                                timeProvider.GetUtcNow(),
                                fraction: 0.95,
                                totalDuration: totalDuration),
                            CancellationToken.None)
                        .ConfigureAwait(false);
                })
            .ConfigureAwait(false);

        try
        {
            await RetryAsync(
                    "artifact",
                    maxAttempts,
                    onRetry,
                    () => context.AddArtifactAsync(artifact, CancellationToken.None).AsTask())
                .ConfigureAwait(false);
        }
        catch
        {
            context.RecordPostCommitWarning(
                "Output committed but artifact metadata could not be persisted.");
            throw;
        }

        try
        {
            await RetryAsync(
                    "completed progress",
                    maxAttempts,
                    onRetry,
                    async () =>
                    {
                        await context.ReportProgressAsync(
                                MediaJobProgress.Create(
                                    "completed",
                                    timeProvider.GetUtcNow(),
                                    fraction: 1,
                                    totalDuration: totalDuration),
                                CancellationToken.None)
                            .ConfigureAwait(false);
                    })
                .ConfigureAwait(false);
        }
        catch
        {
            context.RecordPostCommitWarning(
                "Output and artifact committed but final progress metadata could not be persisted.");
        }
    }

    private static async Task RetryAsync(
        string operation,
        int maxAttempts,
        Func<Exception, string, int, int, Task>? onRetry,
        Func<Task> action)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await action().ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                last = ex;
                if (onRetry is not null)
                {
                    await onRetry(ex, operation, attempt, maxAttempts).ConfigureAwait(false);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw last ?? new InvalidOperationException($"Post-commit {operation} failed.");
    }
}
