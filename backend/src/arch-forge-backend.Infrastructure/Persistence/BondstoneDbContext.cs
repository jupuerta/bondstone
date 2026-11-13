using bondstone_backend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace bondstone_backend.Infrastructure.Persistence;

public class BondstoneDbContext : DbContext
{
    public BondstoneDbContext(DbContextOptions<BondstoneDbContext> options) : base(options)
    {
    }

    public DbSet<Architecture> Architectures => Set<Architecture>();
    public DbSet<Node> Nodes => Set<Node>();
    public DbSet<Connection> Connections => Set<Connection>();

    public DbSet<ArchitectureSnapshot> ArchitectureSnapshots => Set<ArchitectureSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BondstoneDbContext).Assembly);
    }
}
