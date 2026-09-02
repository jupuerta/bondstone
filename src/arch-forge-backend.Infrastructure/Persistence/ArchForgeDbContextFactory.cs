using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace arch_forge_backend.Infrastructure.Persistence;

// Design-time only: `dotnet ef migrations add` / `database update` build a context through this and
// nothing else does. It used to carry the same localhost credential the API shipped with, which is
// how one password ended up written down in three places — so it reads the environment like every
// other caller, and says what to set when it is empty rather than guessing.
public class ArchForgeDbContextFactory : IDesignTimeDbContextFactory<ArchForgeDbContext>
{
    public ArchForgeDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ArchForge");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__ArchForge before running EF tooling, e.g.\n" +
                "  ConnectionStrings__ArchForge=\"Host=localhost;Port=5432;Database=archforge;Username=archforge;Password=$POSTGRES_PASSWORD\" dotnet ef ...");
        }

        var optionsBuilder = new DbContextOptionsBuilder<ArchForgeDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new ArchForgeDbContext(optionsBuilder.Options);
    }
}
