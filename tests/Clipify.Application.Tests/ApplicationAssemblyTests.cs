using Clipify.Application;

namespace Clipify.Application.Tests;

public class ApplicationAssemblyTests
{
    [Fact]
    public void Application_assembly_marker_name_is_stable()
    {
        Assert.Equal("Clipify.Application", ApplicationAssembly.Name);
    }
}
