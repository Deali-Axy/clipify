namespace Clipify.Domain.Jobs;

/// <summary>
/// Immutable observation of a job's persisted state. Application and UI consume snapshots only.
/// </summary>
public sealed record MediaJobSnapshot(
    MediaJobId Id,
    MediaJobState State,
    string DefinitionKind,
    MediaJobDefinition Definition,
    int Priority,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    MediaJobId? RetryOfJobId,
    MediaWorkflowId? WorkflowId,
    MediaJobId? ParentJobId,
    string? Stage,
    MediaJobProgress? Progress,
    string? ErrorCode,
    string? ErrorMessage,
    string? LogPath,
    string? LeaseOwner,
    DateTimeOffset? LeaseAcquiredAt,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset? HeartbeatAt,
    DateTimeOffset? CancelRequestedAt,
    IReadOnlyList<MediaArtifact> Artifacts)
{
    public bool IsTerminal => MediaJobStateTransitions.IsTerminal(State);

    public MediaJobSnapshot WithState(
        MediaJobState state,
        DateTimeOffset updatedAt,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null,
        string? errorCode = null,
        string? errorMessage = null,
        string? stage = null)
    {
        MediaJobStateTransitions.EnsureCanTransition(State, state);

        return this with
        {
            State = state,
            UpdatedAt = updatedAt,
            StartedAt = startedAt ?? StartedAt,
            CompletedAt = completedAt ?? CompletedAt,
            ErrorCode = errorCode ?? ErrorCode,
            ErrorMessage = errorMessage ?? ErrorMessage,
            Stage = stage ?? Stage,
        };
    }

    public static MediaJobSnapshot CreateQueued(
        MediaJobId id,
        MediaJobDefinition definition,
        DateTimeOffset createdAt,
        MediaJobId? retryOfJobId = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new MediaJobSnapshot(
            Id: id,
            State: MediaJobState.Queued,
            DefinitionKind: definition.Kind,
            Definition: definition,
            Priority: definition.Priority,
            CreatedAt: createdAt,
            UpdatedAt: createdAt,
            StartedAt: null,
            CompletedAt: null,
            RetryOfJobId: retryOfJobId,
            WorkflowId: definition.WorkflowId,
            ParentJobId: definition.ParentJobId,
            Stage: null,
            Progress: null,
            ErrorCode: null,
            ErrorMessage: null,
            LogPath: null,
            LeaseOwner: null,
            LeaseAcquiredAt: null,
            LeaseExpiresAt: null,
            HeartbeatAt: null,
            CancelRequestedAt: null,
            Artifacts: []);
    }
}
