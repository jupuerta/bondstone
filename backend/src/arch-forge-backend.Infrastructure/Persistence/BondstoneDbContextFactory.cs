using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace bondstone_backend.Infrastructure.Persistence;

// Design-time only: `dotnet ef migrations add` / `database update` build a context through this and
// nothing else does. It used to carry the same localhost credential the API shipped with, which is
// how one password ended up written down in three places — so it reads the environment like every
// other caller, and says what to set when it is empty rather than guessing.
public class BondstoneDbContextFactory : IDesignTimeDbContextFactory<BondstoneDbContext>
{
    public BondstoneDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Bondstone");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__Bondstone before running EF tooling, e.g.\n" +
                "  ConnectionStrings__Bondstone=\"Host=localhost;Port=5432;Database=bondstone;Username=bondstone;Password=$POSTGRES_PASSWORD\" dotnet ef ...");
        }

        var optionsBuilder = new DbContextOptionsBuilder<BondstoneDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new BondstoneDbContext(optionsBuilder.Options);
    }
}
