using System.Text.Json;
using arch_forge_backend.Application.Configuration.Schemas;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// The two node fields V3 activates (D3.5), read the same forgiving way
/// <see cref="Analysis.ArchitectureGraph.ResilienceOf"/> reads the edge's: saving already validated
/// the schema, so an unreadable configuration means "nothing configured" rather than a failed
/// simulation.
/// </summary>
public static class NodeSettings
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>D4.7: consumer redeliveries. 0 when there are none.</summary>
    public static int QueueRetries(Node node) =>
        node.Type == NodeType.Queue ? Read<QueueConfiguration>(node)?.MaxRetries ?? 0 : 0;

    /// <summary>D4.8: the field that decides whether a dead instance stops at the balancer or climbs past it.</summary>
    public static bool HasHealthCheck(Node node) =>
        node.Type == NodeType.LoadBalancer && Read<LoadBalancerConfiguration>(node)?.HealthCheck == true;

    /// <summary>
    /// V4 DT.1. The declared load of an entry point, in requests per second, already capped by the
    /// gateway's rate limit where there is one (DT.3).
    ///
    /// <strong>Absent is zero</strong>, and zero is what makes every architecture drawn before V4
    /// behave exactly as it did: no load, no utilisation, no queueing factor, same numbers to the
    /// digit (DT.6).
    /// </summary>
    public static decimal DeclaredRps(Node node) => node.Type switch
    {
        NodeType.Api => Read<ApiConfiguration>(node)?.RequestsPerSecond ?? 0,
        NodeType.LoadBalancer => Read<LoadBalancerConfiguration>(node)?.RequestsPerSecond ?? 0,
        NodeType.Gateway => Admitted(Read<GatewayConfiguration>(node)),
        _ => 0
    };

    /// <summary>DT.4: how many copies of itself a component is, which is what multiplies its capacity.</summary>
    public static int Instances(Node node) => node.Type switch
    {
        NodeType.Api => Positive(Read<ApiConfiguration>(node)?.Instances),
        NodeType.Service => Positive(Read<ServiceConfiguration>(node)?.Instances),
        NodeType.Worker => Positive(Read<WorkerConfiguration>(node)?.Instances),

        // A database's copies are its replicas, and they are already two separate fields the
        // analyzer reads (D3.5). Read replicas count because serving reads is capacity.
        NodeType.Database => Read<DatabaseConfiguration>(node) is { } db
            ? 1 + Math.Max(0, db.Replicas ?? 0) + Math.Max(0, db.ReadReplicas ?? 0)
            : 1,

        // V8 §6.2. An ExternalService lands here and answers 1, and that is not a fallthrough:
        // it is the property that makes the type worth having. Nothing anybody writes in its
        // configuration multiplies its capacity, because it is not theirs to scale.
        _ => 1
    };

    /// <summary>V4 D5.2: whether the front door asks who you are. Only Api and Gateway can be one.</summary>
    public static bool HasAuthentication(Node node) => node.Type switch
    {
        NodeType.Api => Read<ApiConfiguration>(node)?.Authentication == true,
        NodeType.Gateway => Read<GatewayConfiguration>(node)?.Authentication == true,
        _ => false
    };

    /// <summary>V4 D5.2: where a message goes when it cannot be processed. Without one it goes nowhere, quietly.</summary>
    public static bool HasDeadLetterQueue(Node node) =>
        node.Type == NodeType.Queue && Read<QueueConfiguration>(node)?.DeadLetterQueue == true;

    /// <summary>V4 D5.2: the rate limit as configured, for the rule that asks whether there is one.</summary>
    public static int? RateLimitOf(Node node) =>
        node.Type == NodeType.Gateway ? Read<GatewayConfiguration>(node)?.RateLimit : null;

    /// <summary>DT.3: the share of incoming traffic a cache answers by itself, so it never reaches what is behind.</summary>
    public static decimal HitRatio(Node node)
    {
        if (node.Type != NodeType.Cache)
            return 0m;

        var configured = Read<CacheConfiguration>(node)?.HitRatio;
        return configured is null ? CapacityModel.DefaultHitRatio : Math.Clamp(configured.Value, 0m, 1m);
    }

    /// <summary>DT.3: what is over the limit is rejected at the door instead of piling onto what is behind.</summary>
    private static decimal Admitted(GatewayConfiguration? gateway)
    {
        var declared = gateway?.RequestsPerSecond ?? 0;
        var limit = gateway?.RateLimit;

        return limit is > 0 ? Math.Min(declared, limit.Value) : declared;
    }

    private static int Positive(int? value) => value is > 0 ? value.Value : 1;

    private static T? Read<T>(Node node) where T : class
    {
        if (string.IsNullOrWhiteSpace(node.Configuration))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(node.Configuration, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
