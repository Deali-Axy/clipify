namespace Clipify.Domain.Jobs;

/// <summary>
/// Centralized legal state transitions. Terminal states are irreversible in place;
/// retry creates a new job linked via <see cref="MediaJobSnapshot.RetryOfJobId"/>.
/// </summary>
public static class MediaJobStateTransitions
{
    private static readonly HashSet<(MediaJobState From, MediaJobState To)> Allowed =
    [
        (MediaJobState.Queued, MediaJobState.Running),
        (MediaJobState.Queued, MediaJobState.Canceled),
        (MediaJobState.Running, MediaJobState.Succeeded),
        (MediaJobState.Running, MediaJobState.Failed),
        (MediaJobState.Running, MediaJobState.Canceling),
        (MediaJobState.Running, MediaJobState.Interrupted),
        (MediaJobState.Canceling, MediaJobState.Canceled),
        (MediaJobState.Canceling, MediaJobState.Interrupted),
        (MediaJobState.Canceling, MediaJobState.Failed),
    ];

    public static bool IsTerminal(MediaJobState state) =>
        state is MediaJobState.Succeeded
            or MediaJobState.Failed
            or MediaJobState.Canceled
            or MediaJobState.Interrupted;

    public static bool CanTransition(MediaJobState from, MediaJobState to) =>
        from == to || Allowed.Contains((from, to));

    public static void EnsureCanTransition(MediaJobState from, MediaJobState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidMediaJobTransitionException(from, to);
        }
    }

    public static bool CanRetryFrom(MediaJobState state) =>
        state is MediaJobState.Failed or MediaJobState.Interrupted or MediaJobState.Canceled;
}
