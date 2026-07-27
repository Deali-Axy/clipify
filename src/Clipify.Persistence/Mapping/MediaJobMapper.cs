using System.Text.Json;
using System.Text.Json.Serialization;
using Clipify.Domain.Jobs;
using Clipify.Persistence.Entities;

namespace Clipify.Persistence.Mapping;

internal static class UnixTime
{
    public static long ToUnixMilliseconds(DateTimeOffset value) => value.ToUniversalTime().ToUnixTimeMilliseconds();

    public static DateTimeOffset FromUnixMilliseconds(long value) =>
        DateTimeOffset.FromUnixTimeMilliseconds(value);

    public static DateTimeOffset? FromUnixMilliseconds(long? value) =>
        value is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(value.Value);
}

internal static class MediaJobMapper
{
    private static readonly JsonSerializerOptions ProgressOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static MediaJobEntity ToEntity(MediaJobSnapshot snapshot)
    {
        return new MediaJobEntity
        {
            Id = snapshot.Id.Value,
            State = snapshot.State.ToString(),
            DefinitionKind = snapshot.DefinitionKind,
            DefinitionJson = MediaJobDefinitionSerializer.Serialize(snapshot.Definition),
            Priority = snapshot.Priority,
            CreatedAtUnixMs = UnixTime.ToUnixMilliseconds(snapshot.CreatedAt),
            UpdatedAtUnixMs = UnixTime.ToUnixMilliseconds(snapshot.UpdatedAt),
            StartedAtUnixMs = snapshot.StartedAt is { } s ? UnixTime.ToUnixMilliseconds(s) : null,
            CompletedAtUnixMs = snapshot.CompletedAt is { } c ? UnixTime.ToUnixMilliseconds(c) : null,
            RetryOfJobId = snapshot.RetryOfJobId?.Value,
            WorkflowId = snapshot.WorkflowId?.Value,
            ParentJobId = snapshot.ParentJobId?.Value,
            Stage = snapshot.Stage,
            ProgressJson = snapshot.Progress is null ? null : SerializeProgress(snapshot.Progress),
            ErrorCode = snapshot.ErrorCode,
            ErrorMessage = snapshot.ErrorMessage,
            LogPath = snapshot.LogPath,
            LeaseOwner = snapshot.LeaseOwner,
            LeaseAcquiredAtUnixMs = snapshot.LeaseAcquiredAt is { } la ? UnixTime.ToUnixMilliseconds(la) : null,
            LeaseExpiresAtUnixMs = snapshot.LeaseExpiresAt is { } le ? UnixTime.ToUnixMilliseconds(le) : null,
            HeartbeatAtUnixMs = snapshot.HeartbeatAt is { } hb ? UnixTime.ToUnixMilliseconds(hb) : null,
            CancelRequestedAtUnixMs = snapshot.CancelRequestedAt is { } cr ? UnixTime.ToUnixMilliseconds(cr) : null,
        };
    }

    public static MediaJobSnapshot ToSnapshot(MediaJobEntity entity, IReadOnlyList<MediaArtifact>? artifacts = null)
    {
        var state = Enum.Parse<MediaJobState>(entity.State, ignoreCase: true);
        var definition = MediaJobDefinitionSerializer.Deserialize(entity.DefinitionKind, entity.DefinitionJson);

        return new MediaJobSnapshot(
            Id: MediaJobId.Parse(entity.Id),
            State: state,
            DefinitionKind: entity.DefinitionKind,
            Definition: definition,
            Priority: entity.Priority,
            CreatedAt: UnixTime.FromUnixMilliseconds(entity.CreatedAtUnixMs),
            UpdatedAt: UnixTime.FromUnixMilliseconds(entity.UpdatedAtUnixMs),
            StartedAt: UnixTime.FromUnixMilliseconds(entity.StartedAtUnixMs),
            CompletedAt: UnixTime.FromUnixMilliseconds(entity.CompletedAtUnixMs),
            RetryOfJobId: entity.RetryOfJobId is null ? null : MediaJobId.Parse(entity.RetryOfJobId),
            WorkflowId: entity.WorkflowId is null ? null : new MediaWorkflowId(entity.WorkflowId),
            ParentJobId: entity.ParentJobId is null ? null : MediaJobId.Parse(entity.ParentJobId),
            Stage: entity.Stage,
            Progress: entity.ProgressJson is null ? null : DeserializeProgress(entity.ProgressJson),
            ErrorCode: entity.ErrorCode,
            ErrorMessage: entity.ErrorMessage,
            LogPath: entity.LogPath,
            LeaseOwner: entity.LeaseOwner,
            LeaseAcquiredAt: UnixTime.FromUnixMilliseconds(entity.LeaseAcquiredAtUnixMs),
            LeaseExpiresAt: UnixTime.FromUnixMilliseconds(entity.LeaseExpiresAtUnixMs),
            HeartbeatAt: UnixTime.FromUnixMilliseconds(entity.HeartbeatAtUnixMs),
            CancelRequestedAt: UnixTime.FromUnixMilliseconds(entity.CancelRequestedAtUnixMs),
            Artifacts: artifacts ?? []);
    }

    public static MediaArtifactEntity ToEntity(MediaArtifact artifact) =>
        new()
        {
            Id = artifact.ArtifactId,
            JobId = artifact.JobId.Value,
            Kind = artifact.Kind,
            Path = artifact.Path,
            SizeBytes = artifact.SizeBytes,
            ContentType = artifact.ContentType,
            CreatedAtUnixMs = UnixTime.ToUnixMilliseconds(artifact.CreatedAt),
        };

    public static MediaArtifact ToArtifact(MediaArtifactEntity entity) =>
        new(
            entity.Id,
            MediaJobId.Parse(entity.JobId),
            entity.Kind,
            entity.Path,
            entity.SizeBytes,
            entity.ContentType,
            UnixTime.FromUnixMilliseconds(entity.CreatedAtUnixMs));

    private static string SerializeProgress(MediaJobProgress progress)
    {
        var dto = new ProgressDto(
            progress.Fraction,
            progress.ProcessedDuration?.TotalMilliseconds,
            progress.TotalDuration?.TotalMilliseconds,
            progress.Speed,
            progress.EstimatedRemaining?.TotalMilliseconds,
            progress.Stage,
            progress.Message,
            UnixTime.ToUnixMilliseconds(progress.UpdatedAt));
        return JsonSerializer.Serialize(dto, ProgressOptions);
    }

    private static MediaJobProgress DeserializeProgress(string json)
    {
        var dto = JsonSerializer.Deserialize<ProgressDto>(json, ProgressOptions)
            ?? throw new InvalidOperationException("Progress JSON deserialized to null.");

        return new MediaJobProgress(
            dto.Fraction,
            dto.ProcessedDurationMs is null ? null : TimeSpan.FromMilliseconds(dto.ProcessedDurationMs.Value),
            dto.TotalDurationMs is null ? null : TimeSpan.FromMilliseconds(dto.TotalDurationMs.Value),
            dto.Speed,
            dto.EstimatedRemainingMs is null ? null : TimeSpan.FromMilliseconds(dto.EstimatedRemainingMs.Value),
            dto.Stage,
            dto.Message,
            UnixTime.FromUnixMilliseconds(dto.UpdatedAtUnixMs));
    }

    private sealed record ProgressDto(
        double? Fraction,
        double? ProcessedDurationMs,
        double? TotalDurationMs,
        double? Speed,
        double? EstimatedRemainingMs,
        string Stage,
        string? Message,
        long UpdatedAtUnixMs);
}
