namespace Clipify.Domain.Jobs;

public sealed class InvalidMediaJobTransitionException : InvalidOperationException
{
    public MediaJobState From { get; }
    public MediaJobState To { get; }

    public InvalidMediaJobTransitionException(MediaJobState from, MediaJobState to)
        : base($"Illegal media job state transition from '{from}' to '{to}'.")
    {
        From = from;
        To = to;
    }
}
