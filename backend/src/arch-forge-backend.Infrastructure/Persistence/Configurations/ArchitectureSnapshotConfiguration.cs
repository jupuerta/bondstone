using bondstone_backend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bondstone_backend.Infrastructure.Persistence.Configurations;

public class ArchitectureSnapshotConfiguration : IEntityTypeConfiguration<ArchitectureSnapshot>
{
    public void Configure(EntityTypeBuilder<ArchitectureSnapshot> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.ScoringVersion).IsRequired().HasMaxLength(10);
        builder.Property(s => s.Summary).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'::jsonb");

        builder.Property(s => s.Areas).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'::jsonb");

        builder.HasIndex(s => new { s.ArchitectureId, s.Version }).IsUnique();

        builder.HasOne<Architecture>()
            .WithMany()
            .HasForeignKey(s => s.ArchitectureId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
