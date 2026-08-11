namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// V3 D3.5. Algorithm went because without traffic round-robin and least-connections are
/// indistinguishable, and Instances because redundancy is topological (D3.4).
/// </summary>
public class LoadBalancerConfiguration
{
    /// <summary>
    /// D4.8: with it the balancer reroutes and absorbs an instance going down, and the fault stops
    /// there; without it, it keeps sending traffic to the dead node and the degradation climbs to
    /// the root. The one field of this schema that changes what you see on screen.
    /// </summary>
    public bool HealthCheck { get; set; }
}
