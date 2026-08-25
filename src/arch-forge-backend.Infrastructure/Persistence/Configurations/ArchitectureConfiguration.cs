using arch_forge_backend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace arch_forge_backend.Infrastructure.Persistence.Configurations;

public class ArchitectureConfiguration : IEntityTypeConfiguration<Architecture>
{
    public void Configure(EntityTypeBuilder<Architecture> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.Property(a => a.ShareToken).IsRequired().HasMaxLength(20);
        builder.HasIndex(a => a.ShareToken).IsUnique();

        builder.Property(a => a.EditToken).IsRequired().HasMaxLength(32);
        builder.HasIndex(a => a.EditToken).IsUnique();
        builder.Property(a => a.Version).IsConcurrencyToken().HasDefaultValue(1);

        builder.HasMany(a => a.Nodes)
            .WithOne()
            .HasForeignKey(n => n.ArchitectureId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.Connections)
            .WithOne()
            .HasForeignKey(c => c.ArchitectureId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
