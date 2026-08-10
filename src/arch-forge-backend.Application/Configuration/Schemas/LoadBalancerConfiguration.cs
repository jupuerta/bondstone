namespace arch_forge_backend.Application.Configuration.Schemas;

public class LoadBalancerConfiguration
{
    public string? Algorithm { get; set; }
    public bool HealthCheck { get; set; }
    public int? Instances { get; set; }
}
