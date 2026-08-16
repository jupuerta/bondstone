using arch_forge_backend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace arch_forge_backend.Infrastructure.Persistence.Configurations;

public class ArchitectureSnapshotConfiguration : IEntityTypeConfiguration<ArchitectureSnapshot>
{
    public void Configure(EntityTypeBuilder<ArchitectureSnapshot> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.ScoringVersion).IsRequired().HasMaxLength(10);
        builder.Property(s => s.Summary).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'::jsonb");

        // What makes writing on every save idempotent: the autosave may retry, and (architecture,
        // version) already identifies the graph without ambiguity (D5.4).
        builder.HasIndex(s => new { s.ArchitectureId, s.Version }).IsUnique();

        builder.HasOne<Architecture>()
            .WithMany()
            .HasForeignKey(s => s.ArchitectureId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
