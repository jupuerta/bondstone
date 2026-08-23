using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// V4 DT.4 and DT.6. Every request-per-second of the product in one place, sibling of
/// <see cref="LatencyModel"/> and, like it, a criterion rather than something the user fills in.
///
/// <strong>The database is the bottleneck by design.</strong> It is twenty-five times more fragile
/// than a cache and ten times more than a gateway, and that ratio is the whole reason the three
/// lessons of the product - put a cache in front, replicate, take it off the request path - have a
/// visible effect without anybody tuning a number.
///
/// Moving one of these means recalculating the four load scenarios of plan técnico §8. They were
/// worked out by hand before this file existed.
/// </summary>
public static class CapacityModel
{
    // Capacity per instance (rps).
    public const int Database = 200;
    public const int Service = 300;
    public const int Api = 500;
    public const int Gateway = 2_000;
    public const int Queue = 5_000;
    public const int Cache = 5_000;
    public const int LoadBalancer = 10_000;
    public const int Worker = 200;
    public const int ObjectStorage = 2_000;

    /// <summary>
    /// V8 §6.2. The same as a Service, and that is the point: it is not <em>worse</em>, it is
    /// <strong>fixed</strong>. <see cref="NodeSettings.Instances"/> answers 1 for this type no
    /// matter what is configured, so the one lever that moves every other component's capacity
    /// does not move this one.
    /// </summary>
    public const int ExternalService = 300;

    /// <summary>
    /// Deliberately enormous: a bottleneck in the metrics pipeline is nobody's lesson, and a
    /// challenge that came out blocked on its own telemetry would be teaching the wrong thing.
    /// </summary>
    public const int Observability = 100_000;

    /// <summary>QA §3.1. The outside has no capacity of yours: it is where load comes from, never where it piles up.</summary>
    public const int Client = 1_000_000;

    /// <summary>
    /// QA §3.2. Enormous for the same reason <see cref="Observability"/> is: a saturated CDN is
    /// nobody's lesson here. What a CDN teaches is absorption - see <see cref="DefaultCdnHitRatio"/> -
    /// not sizing.
    /// </summary>
    public const int Cdn = 100_000;

    /// <summary>QA §3.3. A clock fires work; it barely receives any. The number exists so the switch is total, not because it teaches.</summary>
    public const int Scheduler = 1_000;

    /// <summary>
    /// QA §3.4. The same as a Service on purpose: an IdentityProvider on the synchronous path of
    /// every request should saturate like the service it is, because that is the famous outage.
    /// </summary>
    public const int IdentityProvider = 300;

    /// <summary>DT.5. At or above this, a component is <see cref="HealthState.Degraded"/> - never worse.</summary>
    public const decimal SaturationWarning = 0.8m;

    /// <summary>DT.3. What a cache absorbs when nobody says otherwise.</summary>
    public const decimal DefaultHitRatio = 0.8m;

    /// <summary>
    /// QA §3.2. Higher than a cache's on purpose: a CDN serves static content, and static content
    /// is the best-behaved cacheable traffic there is. The gap between the two defaults is itself
    /// the lesson.
    /// </summary>
    public const decimal DefaultCdnHitRatio = 0.9m;

    public static int PerInstance(NodeType type) => type switch
    {
        NodeType.Database => Database,
        NodeType.Service => Service,
        NodeType.Api => Api,
        NodeType.Gateway => Gateway,
        NodeType.Queue => Queue,
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

    /// <summary>
    /// DT.6, and the correction that made the four scenarios stop telling the same story. The first
    /// version capped the factor at ρ = 0,95, and <em>all four</em> reference scenarios cleared the
    /// cap - the database contributed exactly 400 ms in every one of them, so latency could not tell
    /// a nuisance from a collapse.
    ///
    /// <code>
    /// ρ &lt; 1   →  min( 1/(1−ρ) , 20 )
    /// ρ ≥ 1   →  20 × ρ                     "twenty times slower per 100 % of load"
    /// </code>
    ///
    /// Continuous at ρ = 1, where both branches are ×20, and hand-computable on purpose.
    ///
    /// It is also what composes the two axes without <see cref="EdgeEffect"/> learning a thing: a
    /// component at 1.725 % makes its call cost 6,9 s, a 6,9 s call against a 5 s timeout expires,
    /// and the caller falls through <c>TimeoutExceeded</c> - the path V3 already built. Saturation
    /// never kills anybody directly (DT.5) and can still take down a whole architecture, which is
    /// how it happens in production.
    /// </summary>
    public static decimal QueueingFactor(decimal utilization)
    {
        if (utilization <= 0m)
            return 1m;

        if (utilization >= 1m)
            return 20m * utilization;

        return Math.Min(1m / (1m - utilization), 20m);
    }
}
