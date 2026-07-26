using Clipify.Hosting;

namespace Clipify.Mcp.Tests;

public class McpSkeletonTests
{
    [Fact]
    public void Mcp_shares_hosting_composition_marker()
    {
        Assert.Equal("Clipify.Hosting", HostingAssembly.Name);
    }
}
