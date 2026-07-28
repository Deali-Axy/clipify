using Clipify.FFmpeg;

namespace Clipify.FFmpeg.Tests;

public class FFmpegAssemblyTests
{
    [Fact]
    public void FFmpeg_assembly_marker_name_is_stable()
    {
        Assert.Equal("Clipify.FFmpeg", FFmpegAssembly.Name);
    }
}
