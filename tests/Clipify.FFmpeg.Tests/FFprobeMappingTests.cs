using Clipify.Domain.Media;
using Clipify.FFmpeg;

namespace Clipify.FFmpeg.Tests;

public class FFprobeMappingTests
{
    [Fact]
    public void Maps_multi_stream_media_and_optional_fields()
    {
        var dto = new FFprobeJsonDto
        {
            Format = new FFprobeFormatDto
            {
                FormatName = "mov,mp4,m4a,3gp,3g2,mj2",
                FormatLongName = "QuickTime / MOV",
                Duration = "12.5",
                Size = "1024",
                BitRate = "128000",
            },
            Streams =
            [
                new FFprobeStreamDto
                {
                    Index = 0,
                    CodecType = "video",
                    CodecName = "h264",
                    Width = 1280,
                    Height = 720,
                    AvgFrameRate = "30000/1001",
                    Duration = "12.5",
                },
                new FFprobeStreamDto
                {
                    Index = 1,
                    CodecType = "audio",
                    CodecName = "aac",
                    SampleRate = "48000",
                    Channels = 2,
                    BitRate = "128000",
                    Tags = new Dictionary<string, string> { ["language"] = "eng" },
                },
            ],
        };

        var info = FFprobeClient.Map(@"C:\media\demo.mp4", dto);

        Assert.Equal(TimeSpan.FromSeconds(12.5), info.Duration);
        Assert.Equal(1024, info.SizeBytes);
        Assert.Equal(2, info.Streams.Count);
        Assert.Single(info.VideoStreams);
        Assert.Single(info.AudioStreams);
        Assert.Equal("eng", info.AudioStreams.First().Language);
        Assert.True(info.VideoStreams.First().FrameRate is > 29 and < 30);
    }

    [Fact]
    public void Maps_media_without_audio_and_missing_optional_fields()
    {
        var dto = new FFprobeJsonDto
        {
            Format = new FFprobeFormatDto
            {
                FormatName = "mpegts",
            },
            Streams =
            [
                new FFprobeStreamDto
                {
                    Index = 0,
                    CodecType = "video",
                    CodecName = "mpeg2video",
                    Width = 720,
                    Height = 576,
                    AvgFrameRate = "0/0",
                },
            ],
        };

        var info = FFprobeClient.Map("/tmp/v.ts", dto);

        Assert.Null(info.Duration);
        Assert.Null(info.SizeBytes);
        Assert.Empty(info.AudioStreams);
        Assert.Null(info.VideoStreams.First().FrameRate);
    }
}
