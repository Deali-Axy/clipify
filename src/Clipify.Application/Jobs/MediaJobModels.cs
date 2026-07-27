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

public sealed class MediaJobExecutionContext
{
    public required MediaJobSnapshot Snapshot { get; init; }
    public required Func<MediaJobProgress, CancellationToken, ValueTask> ReportProgressAsync { get; init; }
    public required Func<MediaArtifact, CancellationToken, ValueTask> AddArtifactAsync { get; init; }
}
