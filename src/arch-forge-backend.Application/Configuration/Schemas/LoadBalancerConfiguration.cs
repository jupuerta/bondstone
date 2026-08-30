namespace arch_forge_backend.Application.Configuration.Schemas;

public class LoadBalancerConfiguration
{
    public bool HealthCheck { get; set; }

    public int? RequestsPerSecond { get; set; }

    // QA 30-08 §11.4. «And who balances the balancer?» is answered one layer below the drawing —DNS
    // with several addresses, an anycast IP, a floating one— or by the provider's managed fleet. The
    // model has one lever for all of those: how many of it there are.
    public int? Instances { get; set; }
}
