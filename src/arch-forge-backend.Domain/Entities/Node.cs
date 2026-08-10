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

    /// <summary>
    /// Free-form JSON object, never null (Technical_Design_V1 §5.1: <c>jsonb not null</c>).
    /// A node with nothing configured carries an empty object, not NULL - so readers never
    /// have to distinguish "no configuration" from "configuration absent".
    /// </summary>
    public string Configuration { get; set; } = EmptyConfiguration;

    public const string EmptyConfiguration = "{}";
}
