using arch_forge_backend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace arch_forge_backend.Infrastructure.Persistence;

public class ArchForgeDbContext : DbContext
{
    public ArchForgeDbContext(DbContextOptions<ArchForgeDbContext> options) : base(options)
    {
    }

    public DbSet<Architecture> Architectures => Set<Architecture>();
    public DbSet<Node> Nodes => Set<Node>();
    public DbSet<Connection> Connections => Set<Connection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ArchForgeDbContext).Assembly);
    }
}
