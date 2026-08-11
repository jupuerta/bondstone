namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// V3 D3.5: the three fields that survive for the analyzer alone. MISSING_CACHE reads
/// <see cref="ReadReplicas"/>; killing the Database component kills its replicas too, so none of
/// them changes what the simulator propagates (D4.8).
/// </summary>
public class DatabaseConfiguration
{
    public string? Type { get; set; }
    public int? Replicas { get; set; }
    public int? ReadReplicas { get; set; }
}
