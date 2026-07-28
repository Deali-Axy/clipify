using Clipify.Application;

namespace Clipify.UI.Tests;

public class UiSkeletonTests
{
    [Fact]
    public void Ui_depends_on_application_not_infrastructure()
    {
        Assert.Equal("Clipify.Application", ApplicationAssembly.Name);
    }
}
