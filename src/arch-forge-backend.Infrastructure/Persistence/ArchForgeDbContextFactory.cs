using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace arch_forge_backend.Infrastructure.Persistence;

public class ArchForgeDbContextFactory : IDesignTimeDbContextFactory<ArchForgeDbContext>
{
    public ArchForgeDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ArchForgeDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=archforge;Username=archforge;Password=${POSTGRES_PASSWORD}");

        return new ArchForgeDbContext(optionsBuilder.Options);
    }
}
