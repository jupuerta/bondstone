using bondstone_backend.Domain.Enums;

namespace bondstone_backend.Application.Simulation;

public static class CapacityModel
{
    public const int Database = 200;
    public const int Service = 300;
    public const int Api = 500;
    public const int Gateway = 2_000;
    public const int Queue = 5_000;

    public const int Topic = 5_000;
    public const int Cache = 5_000;
    public const int LoadBalancer = 10_000;
    public const int Worker = 200;
    public const int ObjectStorage = 2_000;

    public const int ExternalService = 300;

    public const int Observability = 100_000;

    public const int Client = 1_000_000;

    public const int Cdn = 100_000;

    public const int Scheduler = 1_000;

    public const int IdentityProvider = 300;

    public const decimal SaturationWarning = 0.8m;

    public const decimal DefaultHitRatio = 0.8m;

    public const decimal DefaultCdnHitRatio = 0.9m;

    public static int PerInstance(NodeType type) => type switch
    {
        NodeType.Database => Database,
        NodeType.Service => Service,
        NodeType.Api => Api,
        NodeType.Gateway => Gateway,
        NodeType.Queue => Queue,
        NodeType.Topic => Topic,
        NodeType.Cache => Cache,
        NodeType.LoadBalancer => LoadBalancer,
        NodeType.Worker => Worker,
        NodeType.ObjectStorage => ObjectStorage,
        NodeType.Observability => Observability,
        NodeType.ExternalService => ExternalService,
        NodeType.Client => Client,
        NodeType.Cdn => Cdn,
        NodeType.Scheduler => Scheduler,
        NodeType.IdentityProvider => IdentityProvider,
        _ => Service
    };

    public static decimal QueueingFactor(decimal utilization)
    {
        if (utilization <= 0m)
            return 1m;

        if (utilization >= 1m)
            return 20m * utilization;

        return Math.Min(1m / (1m - utilization), 20m);
    }
}
