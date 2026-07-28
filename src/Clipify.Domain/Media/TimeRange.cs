namespace Clipify.Domain.Media;

/// <summary>
/// Inclusive start / exclusive end media time range. Both values must be non-negative and Start &lt; End.
/// </summary>
public sealed record TimeRange
{
    public TimeSpan Start { get; }
    public TimeSpan End { get; }

    public TimeRange(TimeSpan start, TimeSpan end)
    {
        if (start < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(start), start, "Start must be non-negative.");
        }

        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), end, "End must be greater than Start.");
        }

        Start = start;
        End = end;
    }

    public TimeSpan Duration => End - Start;

    public static TimeRange FromMilliseconds(long startMs, long endMs) =>
        new(TimeSpan.FromMilliseconds(startMs), TimeSpan.FromMilliseconds(endMs));
}
