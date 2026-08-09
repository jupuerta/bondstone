using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Domain.Entities;

public class Node
{
    public Guid Id { get; set; }
    public Guid ArchitectureId { get; set; }
    public NodeType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public string? Configuration { get; set; }
}
