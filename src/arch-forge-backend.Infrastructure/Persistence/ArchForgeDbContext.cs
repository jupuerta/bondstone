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

    /// <summary>V4 D5.4. The only table V4 adds, and the only place a verdict is stored.</summary>
    public DbSet<ArchitectureSnapshot> ArchitectureSnapshots => Set<ArchitectureSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ArchForgeDbContext).Assembly);
    }
}
