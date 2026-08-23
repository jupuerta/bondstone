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

    /// <summary>Copies of yourself below which there is only one of you (QA V14 B4).</summary>
    private const int RedundantInstances = 2;

    /// <summary>D4.7: consumer redeliveries. 0 when there are none.</summary>
    public static int QueueRetries(Node node) =>
        node.Type == NodeType.Queue ? Read<QueueConfiguration>(node)?.MaxRetries ?? 0 : 0;

    /// <summary>D4.8: the field that decides whether a dead instance stops at the balancer or climbs past it.</summary>
    public static bool HasHealthCheck(Node node) =>
        node.Type == NodeType.LoadBalancer && Read<LoadBalancerConfiguration>(node)?.HealthCheck == true;

    /// <summary>
    /// V4 DT.1. The declared load of an entry point, in requests per second, as declared - the
    /// rate limit is applied by <see cref="Admit"/>, on everything that reaches the door and not
    /// only on what the door itself declares (QA §4).
    ///
    /// <strong>Absent is zero</strong>, and zero is what makes every architecture drawn before V4
    /// behave exactly as it did: no load, no utilisation, no queueing factor, same numbers to the
    /// digit (DT.6).
    /// </summary>
    public static decimal DeclaredRps(Node node) => node.Type switch
    {
        NodeType.Api => Read<ApiConfiguration>(node)?.RequestsPerSecond ?? 0,
        NodeType.LoadBalancer => Read<LoadBalancerConfiguration>(node)?.RequestsPerSecond ?? 0,
        NodeType.Gateway => Read<GatewayConfiguration>(node)?.RequestsPerSecond ?? 0,

        // QA §3.1 / §3.3: where traffic is actually born. A Client declares the users' requests,
        // a Scheduler declares the jobs its clock fires - both flow downstream exactly as an entry
        // point's RPS always has.
        NodeType.Client => Read<ClientConfiguration>(node)?.RequestsPerSecond ?? 0,
        NodeType.Scheduler => Read<SchedulerConfiguration>(node)?.JobsPerSecond ?? 0,
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

    /// <summary>
    /// V4 D5.2: whether the front door asks who you are. The types that can be one are
    /// <see cref="Analysis.Rules.NodeRoles.CanAuthenticate"/>'s - Api, Gateway and, since V14 B6,
    /// Service, because a BFF is a door too.
    ///
    /// V13 D2.1. <strong>Three states, and the third is not a default.</strong> <c>null</c> is
    /// "nobody has said", which is a different claim from <c>false</c> - "there is none" - and the
    /// rule answers them differently. A component that cannot be a front door answers <c>false</c>
    /// rather than <c>null</c>: for it the question is settled, not open.
    /// </summary>
    public static bool? AuthenticationOf(Node node) => node.Type switch
    {
        NodeType.Api => Read<ApiConfiguration>(node)?.Authentication,
        NodeType.Gateway => Read<GatewayConfiguration>(node)?.Authentication,
        NodeType.Service => Read<ServiceConfiguration>(node)?.Authentication,
        _ => false
    };

    /// <summary>V4 D5.2: where a message goes when it cannot be processed. Without one it goes nowhere, quietly.</summary>
    public static bool HasDeadLetterQueue(Node node) =>
        node.Type == NodeType.Queue && Read<QueueConfiguration>(node)?.DeadLetterQueue == true;

    /// <summary>
    /// V4 D5.2: the rate limit as configured, for the rule that asks whether there is one. On a
    /// Gateway or an Api (QA §4) - the two things that can be a door - and nothing else.
    /// </summary>
    public static int? RateLimitOf(Node node) => node.Type switch
    {
        NodeType.Gateway => Read<GatewayConfiguration>(node)?.RateLimit,
        NodeType.Api => Read<ApiConfiguration>(node)?.RateLimit,
        _ => null
    };

    /// <summary>
    /// DT.3: what is over the limit is rejected at the door instead of piling onto what is behind.
    /// Applied to the whole of what reaches the node - declared there or arriving from a Client in
    /// front of it - because a limit that only capped the number typed on the gateway and waved
    /// through the same number typed on the Client was a limit in name only (QA §4).
    /// </summary>
    public static decimal Admit(Node node, decimal load)
    {
        var limit = RateLimitOf(node);
        return limit is > 0 ? Math.Min(load, limit.Value) : load;
    }

    /// <summary>
    /// QA B2: whether a system of record declares the failover replication that SPOF's suggestion
    /// asks for - the field that clears the finding, so the advice and the way out are the same
    /// thing. <strong>Read replicas deliberately do not count</strong>: they spread reads and can
    /// never take a write, which is exactly the asymmetry that makes a system of record a SPOF in
    /// the first place (<see cref="Analysis.Rules.NodeRoles.IsSystemOfRecord"/>). ObjectStorage
    /// answers false because it has no replication field yet - it arrives the day something can
    /// read it, never before (V3 D3.5).
    /// </summary>
    public static bool HasDeclaredReplication(Node node) =>
        node.Type == NodeType.Database && Read<DatabaseConfiguration>(node)?.Replicas is >= 1;

    /// <summary>
    /// QA V14 B4: the same question for a component that holds no state, and the other half of the
    /// doctrine QA B2 wrote down. A system of record's redundancy is declared <em>and never
    /// inferred</em> because two databases are two truths; a stateless component's is declared
    /// <em>as well as</em> inferred from the wiring, because copies of something with no state are
    /// interchangeable by construction - there is no write only one of them can take. SPOF's advice
    /// for these types is "add a second equivalent instance", and until V14 this was the one place
    /// in the product where the field that says so meant nothing.
    ///
    /// <strong>Deliberately not <see cref="Instances"/></strong>, which answers a different
    /// question - how many copies multiply capacity - and defaults to 1 for every type that cannot
    /// declare any. Asking it here would read a Database's read replicas as failover, which is the
    /// exact confusion <see cref="HasDeclaredReplication"/> exists to prevent. Only the three types
    /// that own the field answer at all; a Gateway or a Queue has no <c>instances</c> to declare and
    /// says so (V3 D3.5), so their way out stays the topological one their own suggestion names.
    /// </summary>
    public static bool HasDeclaredInstances(Node node) => node.Type switch
    {
        NodeType.Api => Read<ApiConfiguration>(node)?.Instances is >= RedundantInstances,
        NodeType.Service => Read<ServiceConfiguration>(node)?.Instances is >= RedundantInstances,
        NodeType.Worker => Read<WorkerConfiguration>(node)?.Instances is >= RedundantInstances,
        _ => false
    };

    /// <summary>
    /// DT.3: the share of incoming traffic a cache answers by itself, so it never reaches what is
    /// behind. A Cdn is the same mechanism at the edge (QA §3.2), with its own, higher default.
    /// </summary>
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
