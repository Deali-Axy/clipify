using Clipify.Application.Abstractions;
using Clipify.Application.Parsing;
using Clipify.Domain.Jobs;
using Clipify.Domain.Media;

namespace Clipify.Cli.Parsing;

/// <summary>
/// CLI facade over shared <see cref="BoundaryParsers"/> so MCP and CLI keep identical semantics.
/// </summary>
public static class CliParsers
{
    public static bool TryParseTime(string? text, out TimeSpan value, out ClipifyError? error) =>
        BoundaryParsers.TryParseTime(text, out value, out error);

    public static bool TryParseJobId(string? text, out MediaJobId jobId, out ClipifyError? error) =>
        BoundaryParsers.TryParseJobId(text, out jobId, out error);

    public static bool TryParseConflictPolicy(string? text, out OutputConflictPolicy policy, out ClipifyError? error) =>
        BoundaryParsers.TryParseConflictPolicy(text, out policy, out error);

    public static bool TryParseAudioFormat(string? text, out AudioOutputFormat format, out ClipifyError? error) =>
        BoundaryParsers.TryParseAudioFormat(text, out format, out error);

    public static bool TryParseThumbnailFormat(string? text, out ThumbnailImageFormat format, out ClipifyError? error) =>
        BoundaryParsers.TryParseThumbnailFormat(text, out format, out error);
}
