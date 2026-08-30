using System.Text.Json;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Configuration;
using arch_forge_backend.Application.Configuration.Schemas;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Simulation;

public static class NodeSettings
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private const int RedundantInstances = 2;

    public static int QueueRetries(Node node) =>
        node.Type == NodeType.Queue ? Read<QueueConfiguration>(node)?.MaxRetries ?? 0 : 0;

    public static bool HasHealthCheck(Node node) =>
        node.Type == NodeType.LoadBalancer && Read<LoadBalancerConfiguration>(node)?.HealthCheck == true;

    public static decimal DeclaredRps(Node node) => node.Type switch
    {
        NodeType.Api => Read<ApiConfiguration>(node)?.RequestsPerSecond ?? 0,
        NodeType.LoadBalancer => Read<LoadBalancerConfiguration>(node)?.RequestsPerSecond ?? 0,
        NodeType.Gateway => Read<GatewayConfiguration>(node)?.RequestsPerSecond ?? 0,

        NodeType.Client => Read<ClientConfiguration>(node)?.RequestsPerSecond ?? 0,
        NodeType.Scheduler => Read<SchedulerConfiguration>(node)?.JobsPerSecond ?? 0,
        _ => 0
    };

    public static int Instances(Node node) => node.Type switch
    {
        NodeType.Api => Positive(Read<ApiConfiguration>(node)?.Instances),
        NodeType.Service => Positive(Read<ServiceConfiguration>(node)?.Instances),
        NodeType.Worker => Positive(Read<WorkerConfiguration>(node)?.Instances),

        NodeType.Database => Read<DatabaseConfiguration>(node) is { } db
            ? 1 + Math.Max(0, db.Replicas ?? 0) + Math.Max(0, db.ReadReplicas ?? 0)
            : 1,

        _ => 1
    };

    // The copies that take a write. A read replica serves reads and nothing else; a full replica
    // takes both. For everything that is not a store, every copy takes everything.
    public static int WriteInstances(Node node) => node.Type switch
    {
        NodeType.Database => Read<DatabaseConfiguration>(node) is { } db ? 1 + Math.Max(0, db.Replicas ?? 0) : 1,
        _ => Instances(node)
    };

    public static bool? AuthenticationOf(Node node) => node.Type switch
    {
        NodeType.Api => Read<ApiConfiguration>(node)?.Authentication,
        NodeType.Gateway => Read<GatewayConfiguration>(node)?.Authentication,
        NodeType.Service => Read<ServiceConfiguration>(node)?.Authentication,
        _ => false
    };

    public static bool HasDeadLetterQueue(Node node) =>
        node.Type == NodeType.Queue && Read<QueueConfiguration>(node)?.DeadLetterQueue == true;

    public static int? RateLimitOf(Node node) => node.Type switch
    {
        NodeType.Gateway => Read<GatewayConfiguration>(node)?.RateLimit,
        NodeType.Api => Read<ApiConfiguration>(node)?.RateLimit,
        _ => null
    };

    public static decimal Admit(Node node, decimal load)
    {
        var limit = RateLimitOf(node);
        return limit is > 0 ? Math.Min(load, limit.Value) : load;
    }

    public static bool HasDeclaredReplication(Node node) =>
        node.Type == NodeType.Database && Read<DatabaseConfiguration>(node)?.Replicas is >= 1;

    public static bool HasDeclaredInstances(Node node) => node.Type switch
    {
        NodeType.Api => Read<ApiConfiguration>(node)?.Instances is >= RedundantInstances,
        NodeType.Service => Read<ServiceConfiguration>(node)?.Instances is >= RedundantInstances,
        NodeType.Worker => Read<WorkerConfiguration>(node)?.Instances is >= RedundantInstances,
        _ => false
    };

    // Transversal key of the jsonb, not a property of any schema. Null for a type nobody places or a
    // value outside the closed set, so writes that skip validation (challenge boards, old rows) cannot
    // smuggle a zone into the analysis; "no zone" is the opt-out SpofRule relies on.
    public static string? Zone(Node node)
    {
        if (!NodeRoles.IsYoursToPlace(node.Type))
            return null;

        var zone = Read<PlacementConfiguration>(node)?.Zone;
        return zone is not null && ConfigurationRanges.Zones.Contains(zone) ? zone : null;
    }

    public static decimal HitRatio(Node node)
    {
        var (configured, fallback) = node.Type switch
        {
            NodeType.Cache => (Read<CacheConfiguration>(node)?.HitRatio, CapacityModel.DefaultHitRatio),
            NodeType.Cdn => (Read<CdnConfiguration>(node)?.HitRatio, CapacityModel.DefaultCdnHitRatio),
            _ => (0m, 0m)
        };

        return configured is null ? fallback : Math.Clamp(configured.Value, 0m, 1m);
    }

    private static int Positive(int? value) => value is > 0 ? value.Value : 1;

    private sealed class PlacementConfiguration
    {
        public string? Zone { get; set; }
    }

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
