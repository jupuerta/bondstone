using bondstone_backend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bondstone_backend.Infrastructure.Persistence.Configurations;

public class ConnectionConfiguration : IEntityTypeConfiguration<Connection>
{
    public void Configure(EntityTypeBuilder<Connection> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Configuration)
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValueSql("'{}'::jsonb");

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
