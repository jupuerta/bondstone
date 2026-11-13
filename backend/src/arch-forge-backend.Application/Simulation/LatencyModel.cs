using bondstone_backend.Domain.Enums;

namespace bondstone_backend.Application.Simulation;

public static class LatencyModel
{
    public const int LoadBalancer = 1;
    public const int Cache = 1;
    public const int Queue = 2;

    public const int Topic = 2;
    public const int Api = 5;
    public const int Gateway = 5;
    public const int Service = 10;
    public const int Database = 20;
    public const int Worker = 10;
    public const int ObjectStorage = 15;

    public const int ExternalService = 50;

    public const int Observability = 0;

    public const int Client = 0;

    public const int Cdn = 1;

    public const int Scheduler = 0;

    public const int IdentityProvider = 10;

    public const int Sql = 2;
    public const int Amqp = 3;
    public const int Http = 5;

    public const int UnboundedWait = 30_000;

    public static int Processing(NodeType type) => type switch
    {
        NodeType.LoadBalancer => LoadBalancer,
        NodeType.Cache => Cache,
        NodeType.Queue => Queue,
        NodeType.Topic => Topic,
        NodeType.Api => Api,
        NodeType.Gateway => Gateway,
        NodeType.Service => Service,
        NodeType.Database => Database,
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

    public static int Hop(ProtocolType protocol) => protocol switch
    {
        ProtocolType.Sql => Sql,
        ProtocolType.Amqp => Amqp,
        ProtocolType.Http => Http,
        _ => Http
    };

    public static int TimeoutMs(int timeoutSeconds) => timeoutSeconds * 1_000;
}
