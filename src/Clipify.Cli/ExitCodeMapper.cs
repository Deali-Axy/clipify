using Clipify.Application.Abstractions;
using Clipify.Domain.Jobs;

namespace Clipify.Cli;

public static class ExitCodeMapper
{
    public static int FromError(ClipifyError error) => FromErrorCode(error.Code);

    public static int FromErrorCode(ClipifyErrorCode code) => code switch
    {
        ClipifyErrorCode.Validation => CliExitCode.ValidationError,
        ClipifyErrorCode.NotFound => CliExitCode.FileError,
        ClipifyErrorCode.Conflict => CliExitCode.ValidationError,
        ClipifyErrorCode.InvalidState => CliExitCode.ValidationError,
        ClipifyErrorCode.Canceled => CliExitCode.Canceled,
        ClipifyErrorCode.Interrupted => CliExitCode.Interrupted,
        ClipifyErrorCode.Internal => CliExitCode.InternalError,
        ClipifyErrorCode.HandlerFailed => CliExitCode.JobFailed,
        ClipifyErrorCode.FfmpegUnavailable => CliExitCode.ToolUnavailable,
        ClipifyErrorCode.FfmpegFailed => CliExitCode.JobFailed,
        ClipifyErrorCode.MediaProbeFailed => CliExitCode.JobFailed,
        ClipifyErrorCode.OutputConflict => CliExitCode.JobFailed,
        _ => CliExitCode.InternalError,
    };

    public static int FromJobState(MediaJobState state) => state switch
    {
        MediaJobState.Succeeded => CliExitCode.Success,
        MediaJobState.Failed => CliExitCode.JobFailed,
        MediaJobState.Canceled => CliExitCode.Canceled,
        MediaJobState.Interrupted => CliExitCode.Interrupted,
        _ => CliExitCode.InternalError,
    };

    public static int FromJobSnapshot(MediaJobSnapshot snapshot)
    {
        if (snapshot.State == MediaJobState.Failed
            && !string.IsNullOrWhiteSpace(snapshot.ErrorCode)
            && Enum.TryParse<ClipifyErrorCode>(snapshot.ErrorCode, ignoreCase: true, out var code))
        {
            return FromErrorCode(code);
        }

        return FromJobState(snapshot.State);
    }
}
