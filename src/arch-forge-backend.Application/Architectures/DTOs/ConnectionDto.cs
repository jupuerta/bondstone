using System.Text.Json;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Architectures.DTOs;

public class ConnectionDto
{
    public Guid Id { get; set; }
    public Guid SourceNodeId { get; set; }
    public Guid TargetNodeId { get; set; }
    public ProtocolType Protocol { get; set; }
    public bool Synchronous { get; set; }

    // V2 D3.1, breaking change: timeoutSeconds and retries move inside this object, alongside
    // circuitBreaker and fallback. V3 D4.6 drops bulkhead - old rows keep the key, nobody reads it.
    public JsonElement? Configuration { get; set; }
}
