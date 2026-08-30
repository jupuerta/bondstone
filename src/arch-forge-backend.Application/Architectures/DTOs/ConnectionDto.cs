using System.Text.Json;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Architectures.DTOs;

public class ConnectionDto
{
    public Guid Id { get; set; }
    public Guid SourceNodeId { get; set; }
    public Guid TargetNodeId { get; set; }
    // Derived from the two ends (Protocols.Between); ignored on input, always filled on output.
    public ProtocolType Protocol { get; set; }
    public bool Synchronous { get; set; }

    public JsonElement? Configuration { get; set; }
}
