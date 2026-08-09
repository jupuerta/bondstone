using arch_forge_backend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace arch_forge_backend.Infrastructure.Persistence.Configurations;

public class ConnectionConfiguration : IEntityTypeConfiguration<Connection>
{
    public void Configure(EntityTypeBuilder<Connection> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Protocol).HasConversion<string>().HasMaxLength(50);

        // Technical_Design_V1 §5.2: an edge always resolves to a real node. The PUT replaces the
        // whole set in one transaction, so this cascade is mostly a guard for access through
        // other paths (scripts, admin tooling).
        builder.HasOne<Node>()
            .WithMany()
            .HasForeignKey(c => c.SourceNodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Node>()
            .WithMany()
            .HasForeignKey(c => c.TargetNodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.SourceNodeId);
        builder.HasIndex(c => c.TargetNodeId);
    }
}
