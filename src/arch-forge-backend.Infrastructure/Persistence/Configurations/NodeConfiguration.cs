using arch_forge_backend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace arch_forge_backend.Infrastructure.Persistence.Configurations;

public class NodeConfiguration : IEntityTypeConfiguration<Node>
{
    public void Configure(EntityTypeBuilder<Node> builder)
    {
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Name).IsRequired().HasMaxLength(200);
        builder.Property(n => n.Type).HasConversion<string>().HasMaxLength(50);
        builder.Property(n => n.Configuration).HasColumnType("jsonb");
    }
}
