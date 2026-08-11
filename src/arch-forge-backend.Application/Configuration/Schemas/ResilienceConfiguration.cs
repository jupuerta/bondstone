namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// The single schema of <see cref="Domain.Entities.Connection.Configuration"/> (V2 D3.1). Unlike
/// nodes, an edge has one schema regardless of protocol, so it is deserialized directly instead of
/// going through <see cref="NodeConfigurationRegistry"/>.
///
/// V3 D4.6 dropped Bulkhead: it is the only one of the five whose meaning - capping the resources
/// a call may hold - needs a model of resources, which D3.3 ruled out. It was persisted, validated
/// and painted, and not a single rule read it. What is left is the four mechanisms the simulator
/// actually executes, and this is now the only surface where resilience is configured.
/// </summary>
public class ResilienceConfiguration
{
    /// <summary>
    /// D4.2, the key to all the others: without a timeout no mechanism of the edge can act, because
    /// there is no error to catch - the call simply never returns.
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    public int? Retries { get; set; }
    public bool CircuitBreaker { get; set; }
    public bool Fallback { get; set; }
}
