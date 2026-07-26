using Clipify.Persistence;

namespace Clipify.Persistence.Tests;

public class PersistenceAssemblyTests
{
    [Fact]
    public void Persistence_assembly_marker_name_is_stable()
    {
        Assert.Equal("Clipify.Persistence", PersistenceAssembly.Name);
    }
}
