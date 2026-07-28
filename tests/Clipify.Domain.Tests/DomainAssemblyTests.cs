using Clipify.Domain;

namespace Clipify.Domain.Tests;

public class DomainAssemblyTests
{
    [Fact]
    public void Domain_assembly_marker_name_is_stable()
    {
        Assert.Equal("Clipify.Domain", DomainAssembly.Name);
    }
}
