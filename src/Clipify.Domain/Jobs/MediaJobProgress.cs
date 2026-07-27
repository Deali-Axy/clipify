namespace Clipify.Domain.Jobs;

public sealed record MediaJobProgress(
    double? Fraction,
    TimeSpan? ProcessedDuration,
    TimeSpan? TotalDuration,
    double? Speed,
    TimeSpan? EstimatedRemaining,
    string Stage,
    string? Message,
    DateTimeOffset UpdatedAt)
{
    public static MediaJobProgress Create(
        string stage,
        DateTimeOffset updatedAt,
        double? fraction = null,
        TimeSpan? processedDuration = null,
        TimeSpan? totalDuration = null,
        double? speed = null,
        TimeSpan? estimatedRemaining = null,
        string? message = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);

        if (fraction is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "Fraction must be between 0 and 1.");
        }

        return new MediaJobProgress(
            fraction,
            processedDuration,
            totalDuration,
            speed,
            estimatedRemaining,
            stage,
            message,
            updatedAt);
    }
}
