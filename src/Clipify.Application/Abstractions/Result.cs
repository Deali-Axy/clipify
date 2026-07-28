namespace Clipify.Application.Abstractions;

public enum ClipifyErrorCode
{
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    InvalidState = 4,
    Canceled = 5,
    Interrupted = 6,
    Internal = 7,
    HandlerFailed = 8,
}

public sealed record ClipifyError(ClipifyErrorCode Code, string Message, string? Detail = null)
{
    public static ClipifyError Validation(string message, string? detail = null) =>
        new(ClipifyErrorCode.Validation, message, detail);

    public static ClipifyError NotFound(string message) =>
        new(ClipifyErrorCode.NotFound, message);

    public static ClipifyError InvalidState(string message) =>
        new(ClipifyErrorCode.InvalidState, message);

    public static ClipifyError Internal(string message, string? detail = null) =>
        new(ClipifyErrorCode.Internal, message, detail);

    public static ClipifyError HandlerFailed(string message, string? detail = null) =>
        new(ClipifyErrorCode.HandlerFailed, message, detail);
}

public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public ClipifyError? Error { get; }

    private Result(bool isSuccess, T? value, ClipifyError? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, null);

    public static Result<T> Failure(ClipifyError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, default, error);
    }

    public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        if (!IsSuccess)
        {
            return Result<TNew>.Failure(Error!);
        }

        return Result<TNew>.Success(mapper(Value!));
    }
}

public readonly struct Result
{
    public bool IsSuccess { get; }
    public ClipifyError? Error { get; }

    private Result(bool isSuccess, ClipifyError? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, null);

    public static Result Failure(ClipifyError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, error);
    }
}
