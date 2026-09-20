using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Analysis.Rules;
using bondstone_backend.Domain.Enums;

namespace bondstone_backend.Application.Simulation;

public static class CostModel
{
    // Abstract units, anchored on the database = 10 (costmodel V20, Anexo B). Deliberately not
    // proportional to capacity: the lesson is rps per unit, and a cache buys 125x what a database does.
    public const int Database = 10;
    public const int Service = 4;
    public const int Api = 4;
    public const int Gateway = 6;
    public const int Queue = 3;

    public const int Topic = 3;
    public const int Cache = 2;
    public const int LoadBalancer = 3;
    public const int Worker = 3;
    public const int ObjectStorage = 2;

    public const int ExternalService = 0;

    public const int Observability = 2;

    public const int Client = 0;

    public const int Cdn = 4;

    public const int Scheduler = 1;

    public const int IdentityProvider = 3;

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

    // Instances is the same counter CapacityModel reads, so database replicas are copies here exactly
    // as they are capacity there. Changing one side would charge one lever and leave the other free.
    public static int Of(ArchitectureGraph graph) =>
        graph.Nodes
            .Where(node => NodeRoles.IsYoursToPayFor(node.Type))
            .Sum(node => PerInstance(node.Type) * NodeSettings.Instances(node));
}
