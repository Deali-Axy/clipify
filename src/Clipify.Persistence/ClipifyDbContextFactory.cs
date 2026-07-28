using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Clipify.Persistence;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations</c>.
/// </summary>
public sealed class ClipifyDbContextFactory : IDesignTimeDbContextFactory<ClipifyDbContext>
{
    public ClipifyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ClipifyDbContext>()
            .UseSqlite("Data Source=clipify-design.db")
            .Options;
        return new ClipifyDbContext(options);
    }
}
