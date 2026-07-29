using Clipify.Mcp;

namespace Clipify.Mcp.Tests;

public class McpArgumentParserTests
{
    [Fact]
    public void Parses_repeated_allow_roots_and_data_dir()
    {
        Assert.True(McpArgumentParser.TryParse(
            ["--data-dir", "D:\\data", "--allow-root", "D:\\a", "--allow-root", "D:\\b", "--no-file-log"],
            out var options,
            out var error));
        Assert.Null(error);
        Assert.Equal("D:\\data", options!.DataDirectory);
        Assert.Equal(["D:\\a", "D:\\b"], options.AllowedRoots);
        Assert.False(options.EnableFileLogging);
    }

    [Fact]
    public void Defaults_allow_root_to_current_directory_when_omitted()
    {
        Assert.True(McpArgumentParser.TryParse([], out var options, out _));
        Assert.Equal([Environment.CurrentDirectory], options!.AllowedRoots);
    }

    [Fact]
    public void Rejects_unknown_arguments()
    {
        Assert.False(McpArgumentParser.TryParse(["--wat"], out _, out var error));
        Assert.Contains("--wat", error);
    }
}
