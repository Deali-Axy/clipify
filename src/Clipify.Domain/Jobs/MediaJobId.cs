namespace Clipify.Domain.Jobs;

/// <summary>
/// Stable opaque identifier for a media job. Values are lowercase hex GUIDs.
/// </summary>
public readonly struct MediaJobId : IEquatable<MediaJobId>
{
    public string Value { get; }

    public MediaJobId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public static MediaJobId New() => new(Guid.NewGuid().ToString("N"));

    public static MediaJobId Parse(string value) => new(value);

    public static bool TryParse(string? value, out MediaJobId jobId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            jobId = default;
            return false;
        }

        jobId = new MediaJobId(value);
        return true;
    }

    public bool Equals(MediaJobId other) =>
        string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is MediaJobId other && Equals(other);

    public override int GetHashCode() => Value?.GetHashCode(StringComparison.Ordinal) ?? 0;

    public override string ToString() => Value ?? string.Empty;

    public static bool operator ==(MediaJobId left, MediaJobId right) => left.Equals(right);

    public static bool operator !=(MediaJobId left, MediaJobId right) => !left.Equals(right);
}
