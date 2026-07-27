using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Clipify.Persistence;

internal static class SqliteBusyRetry
{
    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        int maxAttempts = 8,
        CancellationToken cancellationToken = default)
    {
        var delayMs = 20;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < maxAttempts && IsBusy(ex))
            {
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                delayMs = Math.Min(delayMs * 2, 500);
            }
        }
    }

    public static Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        int maxAttempts = 8,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            await action(ct).ConfigureAwait(false);
            return true;
        }, maxAttempts, cancellationToken);

    private static bool IsBusy(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is SqliteException sqlite
                && (sqlite.SqliteErrorCode == 5 /* SQLITE_BUSY */ || sqlite.SqliteErrorCode == 6 /* SQLITE_LOCKED */))
            {
                return true;
            }

            if (current is DbUpdateException)
            {
                return IsBusy(current.InnerException ?? current);
            }
        }

        return false;
    }
}
