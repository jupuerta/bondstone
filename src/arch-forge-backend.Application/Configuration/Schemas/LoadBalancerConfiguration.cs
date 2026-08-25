namespace arch_forge_backend.Application.Configuration.Schemas;

public class LoadBalancerConfiguration
{
    public bool HealthCheck { get; set; }

    public int? RequestsPerSecond { get; set; }
}
