namespace Clipify.Domain.Jobs;

/// <summary>
/// Lifecycle states for a media job. Terminal states cannot return to Queued/Running in place.
/// </summary>
public enum MediaJobState
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Canceling = 4,
    Canceled = 5,
    Interrupted = 6,
}
