using System.Globalization;
using Clipify.Application.Media;
using Clipify.Domain.Jobs;
using Clipify.Domain.Media;

namespace Clipify.FFmpeg.Commands;

internal static class FFmpegCommonArguments
{
    public static void AddProgressProtocol(ICollection<string> args)
    {
        args.Add("-nostdin");
        args.Add("-hide_banner");
        args.Add("-nostats");
        args.Add("-stats_period");
        args.Add("0.5");
        args.Add("-progress");
        args.Add("pipe:1");
    }

    public static string FormatTimestamp(TimeSpan value) =>
        value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
}

public sealed class TrimMediaCommandBuilder : IFFmpegCommandBuilder<TrimMediaJobDefinition>
{
    public IReadOnlyList<string> Build(TrimMediaJobDefinition definition, string temporaryOutputPath)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryOutputPath);

        var input = Path.GetFullPath(definition.InputPath);
        var output = Path.GetFullPath(temporaryOutputPath);
        var args = new List<string>();

        FFmpegCommonArguments.AddProgressProtocol(args);
        args.Add("-ss");
        args.Add(FFmpegCommonArguments.FormatTimestamp(definition.Range.Start));
        args.Add("-to");
        args.Add(FFmpegCommonArguments.FormatTimestamp(definition.Range.End));
        args.Add("-i");
        args.Add(input);
        args.Add("-c");
        args.Add("copy");
        args.Add("-y"); // overwrite temp path only; final conflict handled by OutputCommitter
        args.Add(output);
        return args;
    }
}

public sealed class ExtractAudioCommandBuilder : IFFmpegCommandBuilder<ExtractAudioJobDefinition>
{
    public IReadOnlyList<string> Build(ExtractAudioJobDefinition definition, string temporaryOutputPath)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryOutputPath);

        var input = Path.GetFullPath(definition.InputPath);
        var output = Path.GetFullPath(temporaryOutputPath);
        var args = new List<string>();

        FFmpegCommonArguments.AddProgressProtocol(args);
        args.Add("-i");
        args.Add(input);
        args.Add("-vn");

        switch (definition.Format)
        {
            case AudioOutputFormat.Copy:
                args.Add("-acodec");
                args.Add("copy");
                break;
            case AudioOutputFormat.Mp3:
                args.Add("-acodec");
                args.Add("libmp3lame");
                break;
            case AudioOutputFormat.Aac:
                args.Add("-acodec");
                args.Add("aac");
                break;
            case AudioOutputFormat.Wav:
                args.Add("-acodec");
                args.Add("pcm_s16le");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(definition), definition.Format, "Unsupported audio format.");
        }

        args.Add("-y");
        args.Add(output);
        return args;
    }
}

public sealed class ThumbnailCommandBuilder : IFFmpegCommandBuilder<ThumbnailJobDefinition>
{
    public IReadOnlyList<string> Build(ThumbnailJobDefinition definition, string temporaryOutputPath)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryOutputPath);

        var input = Path.GetFullPath(definition.InputPath);
        var output = Path.GetFullPath(temporaryOutputPath);
        var args = new List<string>();

        FFmpegCommonArguments.AddProgressProtocol(args);
        args.Add("-ss");
        args.Add(FFmpegCommonArguments.FormatTimestamp(definition.At));
        args.Add("-i");
        args.Add(input);
        args.Add("-frames:v");
        args.Add("1");

        switch (definition.Format)
        {
            case ThumbnailImageFormat.Jpg:
                args.Add("-f");
                args.Add("image2");
                break;
            case ThumbnailImageFormat.Png:
                args.Add("-f");
                args.Add("image2");
                args.Add("-c:v");
                args.Add("png");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(definition), definition.Format, "Unsupported thumbnail format.");
        }

        args.Add("-y");
        args.Add(output);
        return args;
    }
}
