namespace Clipify.Domain.Jobs;

public sealed record MediaArtifact(
    string ArtifactId,
    MediaJobId JobId,
    string Kind,
    string Path,
    long? SizeBytes,
    string? ContentType,
    DateTimeOffset CreatedAt)
{
    public static MediaArtifact Create(
        MediaJobId jobId,
        string kind,
        string path,
        DateTimeOffset createdAt,
        long? sizeBytes = null,
        string? contentType = null,
        string? artifactId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new MediaArtifact(
            artifactId ?? Guid.NewGuid().ToString("N"),
            jobId,
            kind,
            path,
            sizeBytes,
            contentType,
            createdAt);
    }
}
