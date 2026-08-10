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

    /// <summary>
    /// Resilience settings of the edge, as a JSON object that is never null - symmetric to
    /// <see cref="Node.Configuration"/> (V2 D3.1). It replaces the former TimeoutSeconds and
    /// Retries columns so every rule reads resilience from one place.
    /// </summary>
    public string Configuration { get; set; } = EmptyConfiguration;

    public const string EmptyConfiguration = "{}";
}
