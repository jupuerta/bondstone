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

        // The write credential. Full-length (32 hex chars, unlike ShareToken's 10) because guessing
        // it is the only thing standing between a leaked read link and a write - no other check
        // exists without accounts.
        builder.Property(a => a.EditToken).IsRequired().HasMaxLength(32);
        builder.HasIndex(a => a.EditToken).IsUnique();
        // Technical_Design_V1 §5.1: "Version integer not null default 1". The app always writes
        // the version explicitly; the store default only matters for rows created outside it.
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
