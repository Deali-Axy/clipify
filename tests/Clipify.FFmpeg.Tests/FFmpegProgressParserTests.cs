using Clipify.FFmpeg;

namespace Clipify.FFmpeg.Tests;

public class FFmpegProgressParserTests
{
    [Fact]
    public void Parses_chunked_crlf_and_lf_input()
    {
        var parser = new FFmpegProgressParser();
        parser.Append("frame=1\r\nout_time_us=1000000\r\n");
        parser.Append("speed=1.5x\nprogress=con");
        parser.Append("tinue\n");

        Assert.True(parser.TryDequeueSnapshot(out var snapshot));
        Assert.Equal(TimeSpan.FromSeconds(1), snapshot.OutTime);
        Assert.Equal(1.5, snapshot.Speed);
        Assert.Equal(1, snapshot.Frame);
        Assert.False(snapshot.IsEnd);
        Assert.False(parser.TryDequeueSnapshot(out _));
    }

    [Fact]
    public void Ignores_unknown_and_malformed_keys()
    {
        var parser = new FFmpegProgressParser();
        parser.Append("not-a-kv\n");
        parser.Append("=novalue\n");
        parser.Append("weird=yes\n");
        parser.Append("out_time_us=not-a-number\n");
        parser.Append("progress=continue\n");

        Assert.True(parser.TryDequeueSnapshot(out var snapshot));
        Assert.Null(snapshot.OutTime);
    }

    [Fact]
    public void Prefers_out_time_us_over_out_time_ms()
    {
        var parser = new FFmpegProgressParser();
        parser.Append("out_time_ms=5000\n");
        parser.Append("out_time_us=2000000\n");
        parser.Append("progress=continue\n");

        Assert.True(parser.TryDequeueSnapshot(out var snapshot));
        Assert.Equal(TimeSpan.FromSeconds(2), snapshot.OutTime);
    }

    [Fact]
    public void Uses_out_time_ms_when_us_missing()
    {
        var parser = new FFmpegProgressParser();
        parser.Append("out_time_ms=2500\n");
        parser.Append("progress=continue\n");

        Assert.True(parser.TryDequeueSnapshot(out var snapshot));
        Assert.Equal(TimeSpan.FromMilliseconds(2500), snapshot.OutTime);
    }

    [Fact]
    public void Emits_end_frame()
    {
        var parser = new FFmpegProgressParser();
        parser.Append("out_time_us=3000000\nspeed=2.0\nprogress=end\n");

        Assert.True(parser.TryDequeueSnapshot(out var snapshot));
        Assert.True(snapshot.IsEnd);
        Assert.Equal(2.0, snapshot.Speed);
        Assert.Equal(TimeSpan.FromSeconds(3), snapshot.OutTime);
    }
}
