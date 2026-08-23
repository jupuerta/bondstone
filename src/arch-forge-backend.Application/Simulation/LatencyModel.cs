using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// V3 D3.1. Every millisecond of the product in one place, sibling of
/// <see cref="Analysis.RuleThresholds"/>: a criterion, not something the user fills in. No node and
/// no edge gains a latency field.
///
/// The third reason is the one that closes the discussion: with an editable latency, Challenge #003
/// - "get this under 100 ms" - is solved by typing 100 into a box. With constants, the only way to
/// bring latency down is to change the architecture: shorten the chain, put a cache in front, make
/// a leg asynchronous. Which is exactly the lesson.
///
/// Calibrated so a healthy architecture lives in tens of milliseconds and any failure jumps to
/// seconds. That distance of three orders of magnitude is what makes the panel tell a story at a
/// glance - and it is what the five reference scenarios pin down, so moving a number here means
/// recalculating them (plan técnico §12).
/// </summary>
public static class LatencyModel
{
    // Processing, per node type (ms).
    public const int LoadBalancer = 1;
    public const int Cache = 1;
    public const int Queue = 2;
    public const int Api = 5;
    public const int Gateway = 5;
    public const int Service = 10;
    public const int Database = 20;
    public const int Worker = 10;
    public const int ObjectStorage = 15;

    /// <summary>
    /// V8 §6.2. Five times a Service, because a third party is on the other side of somebody else's
    /// network and somebody else's queues. It is the highest processing time in the model on
    /// purpose: an external call in the middle of a synchronous chain should be visible in the
    /// latency before anybody breaks anything.
    /// </summary>
    public const int ExternalService = 50;

    /// <summary>
    /// V4 D5.3. Telemetry is emitted alongside the request, not in front of it: charging it to the
    /// caller's path would make instrumenting an architecture look like slowing it down, which is
    /// the opposite of the lesson.
    /// </summary>
    public const int Observability = 0;

    /// <summary>QA §3.1. The outside does no processing of yours; the cost of reaching you is its edge's hop.</summary>
    public const int Client = 0;

    /// <summary>QA §3.2. Edge-fast, like the cache it is.</summary>
    public const int Cdn = 1;

    /// <summary>QA §3.3. A trigger, not a hop on anybody's request path.</summary>
    public const int Scheduler = 0;

    /// <summary>QA §3.4. A service doing real work (token validation, lookups) on the request path.</summary>
    public const int IdentityProvider = 10;

    // Network hop, per protocol (ms).
    public const int Sql = 2;
    public const int Amqp = 3;
    public const int Http = 5;

    /// <summary>
    /// What a call with no timeout takes to give up (ms). D4.2: without a timeout the call never
    /// returns, so the retries do not even happen - it is not "like a timeout but worse", it is a
    /// different class of failure.
    /// </summary>
    public const int UnboundedWait = 30_000;

    public static int Processing(NodeType type) => type switch
    {
        NodeType.LoadBalancer => LoadBalancer,
        NodeType.Cache => Cache,
        NodeType.Queue => Queue,
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

    /// <summary>Timeouts are configured in seconds and the whole model counts milliseconds.</summary>
    public static int TimeoutMs(int timeoutSeconds) => timeoutSeconds * 1_000;
}
