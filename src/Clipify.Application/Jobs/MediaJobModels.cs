using Clipify.Domain.Jobs;

namespace Clipify.Application.Jobs;

public sealed record MediaJobChange(
    MediaJobId JobId,
    MediaJobState State,
    DateTimeOffset At,
    MediaJobProgress? Progress = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record MediaJobListQuery(
    IReadOnlyList<MediaJobState>? States = null,
    int Skip = 0,
    int Take = 20);

public sealed record MediaJobClaimOptions(
    string LeaseOwner,
    TimeSpan LeaseDuration,
    int MaxConcurrency);

/// <summary>
/// Result of an atomic cancel attempt. <see cref="StateChanged"/> is true only when the store
/// actually committed a new state; callers must publish events only in that case.
/// </summary>
public sealed record MediaJobCancelOutcome(
    bool Found,
    bool StateChanged,
    MediaJobSnapshot? Snapshot);

public sealed class MediaJobExecutionContext
{
    private int _outputCommitted;

    public required MediaJobSnapshot Snapshot { get; init; }
    public required Func<MediaJobProgress, CancellationToken, ValueTask> ReportProgressAsync { get; init; }
    public required Func<MediaArtifact, CancellationToken, ValueTask> AddArtifactAsync { get; init; }

    /// <summary>
    /// True after the handler has irreversibly produced the job's primary output (file commit or
    /// equivalent side effect). Once set, the executor must finish as <see cref="MediaJobState.Succeeded"/>
    /// even if a cancel was requested.
    /// </summary>
    public bool OutputCommitted => Volatile.Read(ref _outputCommitted) != 0;

    public void MarkOutputCommitted() => Interlocked.Exchange(ref _outputCommitted, 1);

    /// <summary>
    /// Optional warning persisted when the job succeeds after a post-commit metadata repair gap
    /// (for example artifact or final progress could not be written).
    /// </summary>
    public string? PostCommitWarning { get; private set; }

    public void RecordPostCommitWarning(string warning)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warning);
        PostCommitWarning = string.IsNullOrWhiteSpace(PostCommitWarning)
            ? warning
            : $"{PostCommitWarning}; {warning}";
    }
}
