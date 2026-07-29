using System.Globalization;
using Clipify.Application.Abstractions;
using Clipify.Domain.Jobs;
using Clipify.Domain.Media;

namespace Clipify.Application.Parsing;

/// <summary>
/// Shared CLI/MCP boundary parsers. Keep time, JobId, format, and conflict semantics identical.
/// </summary>
public static class BoundaryParsers
{
    public static bool TryParseTime(string? text, out TimeSpan value, out ClipifyError? error)
    {
        value = default;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = ClipifyError.Validation("Time value is required.");
            return false;
        }

        text = text.Trim();

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
        {
            if (ms < 0)
            {
                error = ClipifyError.Validation("Time must be non-negative.", text);
                return false;
            }

            try
            {
                value = TimeSpan.FromMilliseconds(ms);
            }
            catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
            {
                error = ClipifyError.Validation(
                    "Time is out of range for TimeSpan. Use a smaller millisecond value or HH:MM:SS[.fff].",
                    text);
                return false;
            }

            return true;
        }

        if (TimeSpan.TryParseExact(
                text,
                ["hh\\:mm\\:ss\\.fff", "hh\\:mm\\:ss", "h\\:mm\\:ss\\.fff", "h\\:mm\\:ss", "mm\\:ss\\.fff", "mm\\:ss"],
                CultureInfo.InvariantCulture,
                out value))
        {
            if (value < TimeSpan.Zero)
            {
                error = ClipifyError.Validation("Time must be non-negative.", text);
                return false;
            }

            return true;
        }

        // Also accept total-hours form HH:MM:SS where hours may exceed 24 via manual parse.
        if (TryParseTimestamp(text, out value))
        {
            return true;
        }

        error = ClipifyError.Validation(
            "Invalid time. Use HH:MM:SS[.fff] or integer milliseconds.",
            text);
        return false;
    }

    public static bool TryParseJobId(string? text, out MediaJobId jobId, out ClipifyError? error)
    {
        jobId = default;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = ClipifyError.Validation("Job id is required.");
            return false;
        }

        text = text.Trim();
        if (!Guid.TryParseExact(text, "N", out _) && !Guid.TryParse(text, out _))
        {
            error = ClipifyError.Validation("Job id must be a GUID.", text);
            return false;
        }

        // Normalize to N-format lowercase for consistency with MediaJobId.New().
        if (Guid.TryParse(text, out var guid))
        {
            jobId = new MediaJobId(guid.ToString("N"));
            return true;
        }

        error = ClipifyError.Validation("Job id must be a GUID.", text);
        return false;
    }

    public static bool TryParseConflictPolicy(string? text, out OutputConflictPolicy policy, out ClipifyError? error)
    {
        policy = OutputConflictPolicy.Fail;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (Enum.TryParse(text.Trim(), ignoreCase: true, out policy)
            && Enum.IsDefined(policy))
        {
            return true;
        }

        error = ClipifyError.Validation(
            "Invalid conflict policy. Allowed: fail, overwrite, rename, skip.",
            text);
        return false;
    }

    public static bool TryParseAudioFormat(string? text, out AudioOutputFormat format, out ClipifyError? error)
    {
        format = AudioOutputFormat.Mp3;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (Enum.TryParse(text.Trim(), ignoreCase: true, out format)
            && Enum.IsDefined(format))
        {
            return true;
        }

        error = ClipifyError.Validation(
            "Invalid audio format. Allowed: copy, mp3, aac, wav.",
            text);
        return false;
    }

    public static bool TryParseThumbnailFormat(string? text, out ThumbnailImageFormat format, out ClipifyError? error)
    {
        format = ThumbnailImageFormat.Jpg;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            // Infer from output path later; default jpg.
            return true;
        }

        var normalized = text.Trim().TrimStart('.');
        if (normalized.Equals("jpeg", StringComparison.OrdinalIgnoreCase))
        {
            format = ThumbnailImageFormat.Jpg;
            return true;
        }

        if (Enum.TryParse(normalized, ignoreCase: true, out format)
            && Enum.IsDefined(format))
        {
            return true;
        }

        error = ClipifyError.Validation(
            "Invalid thumbnail format. Allowed: jpg, png.",
            text);
        return false;
    }

    public static bool TryParseJobStates(string? text, out IReadOnlyList<MediaJobState>? states, out ClipifyError? error)
    {
        states = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var parts = text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return true;
        }

        var list = new List<MediaJobState>(parts.Length);
        foreach (var part in parts)
        {
            if (!Enum.TryParse(part, ignoreCase: true, out MediaJobState state) || !Enum.IsDefined(state))
            {
                error = ClipifyError.Validation(
                    "Invalid job state filter. Allowed: queued, running, succeeded, failed, canceling, canceled, interrupted.",
                    part);
                return false;
            }

            list.Add(state);
        }

        states = list;
        return true;
    }

    private static bool TryParseTimestamp(string text, out TimeSpan value)
    {
        value = default;
        var parts = text.Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        try
        {
            if (parts.Length == 2)
            {
                var minutes = int.Parse(parts[0], CultureInfo.InvariantCulture);
                var seconds = double.Parse(parts[1], CultureInfo.InvariantCulture);
                if (minutes < 0 || seconds < 0)
                {
                    return false;
                }

                value = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
                return true;
            }

            var hours = int.Parse(parts[0], CultureInfo.InvariantCulture);
            var mins = int.Parse(parts[1], CultureInfo.InvariantCulture);
            var secs = double.Parse(parts[2], CultureInfo.InvariantCulture);
            if (hours < 0 || mins < 0 || secs < 0)
            {
                return false;
            }

            value = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(mins) + TimeSpan.FromSeconds(secs);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
