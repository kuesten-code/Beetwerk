using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kuestencode.Beetwerk.Data;

/// <summary>Nur für <c>dotnet ef</c> zur Entwurfszeit.</summary>
public class BeetwerkDbContextFactory : IDesignTimeDbContextFactory<BeetwerkDbContext>
{
    public BeetwerkDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<BeetwerkDbContext>().UseSqlite("Data Source=design.db").Options);
}
