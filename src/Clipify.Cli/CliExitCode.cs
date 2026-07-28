namespace Clipify.Cli;

/// <summary>
/// Stable process exit codes. Mapped from ErrorCode/JobState — never from message text.
/// </summary>
public static class CliExitCode
{
    public const int Success = 0;
    public const int InternalError = 1;
    public const int ValidationError = 2;
    public const int FileError = 3;
    public const int ToolUnavailable = 4;
    public const int JobFailed = 5;
    public const int Canceled = 6;
    public const int Interrupted = 7;
}
