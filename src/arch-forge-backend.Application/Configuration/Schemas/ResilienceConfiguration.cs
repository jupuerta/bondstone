namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// The single schema of <see cref="Domain.Entities.Connection.Configuration"/> (V2 D3.1). Unlike
/// nodes, an edge has one schema regardless of protocol, so it is deserialized directly instead of
/// going through <see cref="NodeConfigurationRegistry"/>.
/// </summary>
public class ResilienceConfiguration
{
    public int? TimeoutSeconds { get; set; }
    public int? Retries { get; set; }
    public bool CircuitBreaker { get; set; }
    public bool Fallback { get; set; }
    public bool Bulkhead { get; set; }
}
