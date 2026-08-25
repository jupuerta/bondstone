using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Domain.Entities;

public class Connection
{
    public Guid Id { get; set; }
    public Guid ArchitectureId { get; set; }
    public Guid SourceNodeId { get; set; }
    public Guid TargetNodeId { get; set; }
    public ProtocolType Protocol { get; set; }
    public bool Synchronous { get; set; }

    public string Configuration { get; set; } = EmptyConfiguration;

    public const string EmptyConfiguration = "{}";
}
