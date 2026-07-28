using Clipify.Domain.Jobs;
using Clipify.Domain.Media;
using Clipify.FFmpeg.Commands;

namespace Clipify.FFmpeg.Tests;

public class MediaCommandBuilderTests
{
    [Theory]
    [InlineData(@"C:\media\my video.mp4", @"C:\out\clip.mp4")]
    [InlineData(@"C:\媒体\测试 'quote'.mp4", @"C:\输出\结果.mp4")]
    [InlineData(@"C:\weird\a&b;(c)|d.mp4", @"C:\weird\out.mp4")]
    public void Trim_builder_returns_argument_list_with_raw_paths(string input, string output)
    {
        var builder = new TrimMediaCommandBuilder();
        var definition = new TrimMediaJobDefinition
        {
            InputPath = input,
            OutputPath = output,
            Range = TimeRange.FromMilliseconds(1000, 4000),
        };

        var temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, ".tmp.partial.mp4");
        var args = builder.Build(definition, temp).ToList();

        Assert.Contains("-progress", args);
        Assert.Contains("pipe:1", args);
        Assert.Contains("-nostdin", args);
        Assert.DoesNotContain(args, a => a.Contains(' ') && a.StartsWith("-", StringComparison.Ordinal));
        Assert.Equal(Path.GetFullPath(input), args[args.IndexOf("-i") + 1]);
        Assert.Equal(Path.GetFullPath(temp), args[^1]);
        Assert.Equal("00:00:01.000", args[args.IndexOf("-ss") + 1]);
        Assert.Equal("00:00:04.000", args[args.IndexOf("-to") + 1]);
        Assert.All(args, a => Assert.False(a.Contains("ffmpeg ", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Extract_audio_builder_uses_format_enum_not_raw_codec_string_from_caller()
    {
        var builder = new ExtractAudioCommandBuilder();
        var definition = new ExtractAudioJobDefinition
        {
            InputPath = "/tmp/in.mp4",
            OutputPath = "/tmp/out.mp3",
            Format = AudioOutputFormat.Mp3,
        };

        var args = builder.Build(definition, "/tmp/.partial.mp3");
        Assert.Contains("-vn", args);
        Assert.Contains("libmp3lame", args);
        Assert.DoesNotContain(args, a => a.Contains(';', StringComparison.Ordinal));
    }

    [Fact]
    public void Thumbnail_builder_seeks_and_takes_one_frame()
    {
        var builder = new ThumbnailCommandBuilder();
        var definition = new ThumbnailJobDefinition
        {
            InputPath = "/tmp/in.mp4",
            OutputPath = "/tmp/t.jpg",
            At = TimeSpan.FromMilliseconds(1500),
            Format = ThumbnailImageFormat.Jpg,
        };

        var args = builder.Build(definition, "/tmp/.partial.jpg").ToList();
        Assert.Equal("00:00:01.500", args[args.IndexOf("-ss") + 1]);
        Assert.Equal("1", args[args.IndexOf("-frames:v") + 1]);
    }

    [Fact]
    public void FormatTimestamp_does_not_wrap_after_24_hours()
    {
        Assert.Equal("25:30:00.500", FFmpegCommonArguments.FormatTimestamp(
            TimeSpan.FromHours(25) + TimeSpan.FromMinutes(30) + TimeSpan.FromMilliseconds(500)));
        Assert.Equal("00:00:00.000", FFmpegCommonArguments.FormatTimestamp(TimeSpan.Zero));
        Assert.Equal("100:00:00.000", FFmpegCommonArguments.FormatTimestamp(TimeSpan.FromHours(100)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FFmpegCommonArguments.FormatTimestamp(TimeSpan.FromSeconds(-1)));
    }
}
