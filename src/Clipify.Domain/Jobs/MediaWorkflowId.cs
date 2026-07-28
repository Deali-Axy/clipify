namespace Clipify.Domain.Jobs;

/// <summary>
/// Optional workflow grouping identifier. Reserved for multi-stage jobs; not a DAG engine.
/// </summary>
public readonly struct MediaWorkflowId : IEquatable<MediaWorkflowId>
{
    public string Value { get; }

    public MediaWorkflowId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public static MediaWorkflowId New() => new(Guid.NewGuid().ToString("N"));

    public bool Equals(MediaWorkflowId other) =>
        string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is MediaWorkflowId other && Equals(other);

    public override int GetHashCode() => Value?.GetHashCode(StringComparison.Ordinal) ?? 0;

    public override string ToString() => Value ?? string.Empty;

    public static bool operator ==(MediaWorkflowId left, MediaWorkflowId right) => left.Equals(right);

    public static bool operator !=(MediaWorkflowId left, MediaWorkflowId right) => !left.Equals(right);
}
