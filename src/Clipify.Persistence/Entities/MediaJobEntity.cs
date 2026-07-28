namespace Clipify.Persistence.Entities;

public sealed class MediaJobEntity
{
    public required string Id { get; set; }
    public required string State { get; set; }
    public required string DefinitionKind { get; set; }
    public required string DefinitionJson { get; set; }
    public int Priority { get; set; }
    public long CreatedAtUnixMs { get; set; }
    public long UpdatedAtUnixMs { get; set; }
    public long? StartedAtUnixMs { get; set; }
    public long? CompletedAtUnixMs { get; set; }
    public string? RetryOfJobId { get; set; }
    public string? WorkflowId { get; set; }
    public string? ParentJobId { get; set; }
    public string? Stage { get; set; }
    public string? ProgressJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? LogPath { get; set; }
    public string? LeaseOwner { get; set; }
    public long? LeaseAcquiredAtUnixMs { get; set; }
    public long? LeaseExpiresAtUnixMs { get; set; }
    public long? HeartbeatAtUnixMs { get; set; }
    public long? CancelRequestedAtUnixMs { get; set; }

    public List<MediaArtifactEntity> Artifacts { get; set; } = [];
}

public sealed class MediaArtifactEntity
{
    public required string Id { get; set; }
    public required string JobId { get; set; }
    public required string Kind { get; set; }
    public required string Path { get; set; }
    public long? SizeBytes { get; set; }
    public string? ContentType { get; set; }
    public long CreatedAtUnixMs { get; set; }

    public MediaJobEntity? Job { get; set; }
}
