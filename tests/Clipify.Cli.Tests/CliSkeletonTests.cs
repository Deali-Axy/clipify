using Clipify.Hosting;

namespace Clipify.Cli.Tests;

public class CliSkeletonTests
{
    [Fact]
    public void Cli_shares_hosting_composition_marker()
    {
        Assert.Equal("Clipify.Hosting", HostingAssembly.Name);
    }
}
