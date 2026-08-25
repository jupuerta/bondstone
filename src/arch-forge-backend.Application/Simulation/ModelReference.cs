using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// V16 C2. The model, read out loud.
///
/// Every number in <see cref="CapacityModel"/> and <see cref="LatencyModel"/> decides part of a
/// score — Escalabilidad is computed out of these and nothing else — and until this file existed
/// none of them was reachable without opening the repository. A veredicto that rests on a figure
/// the reader cannot look up is not teaching anybody anything: it is <c>UNDERSIZED_COMPONENT</c>
/// arriving as an opinion.
///
/// <strong>Nothing here is a number.</strong> Every value is asked of the class that owns it, and
/// the two switches are walked with <c>Enum.GetValues</c> rather than listed — a type added to
/// <see cref="NodeType"/> appears here on its own, and a type whose capacity moves moves here too.
/// A published copy that drifts from the model would be worse than the silence it replaces, and
/// Anexo B of V16 is what that drift looks like: <see cref="NodeType.Topic"/> has no entry in
/// <see cref="LatencyModel.Processing"/> and falls through to a Service's 10 ms, a figure nobody
/// chose. This endpoint does not fix it — it is what makes it visible.
/// </summary>
public static class ModelReference
{
    /// <summary>
    /// The grid the queueing curve is sampled on. Fine enough that the hyperbola draws as a curve
    /// rather than a polygon, and chosen so that both figures anybody annotates land exactly on a
    /// sample rather than between two: <see cref="CapacityModel.SaturationWarning"/> (0,800, where
    /// the component turns <see cref="HealthState.Degraded"/>) and 1,725, the utilisation
    /// <see cref="CapacityModel.QueueingFactor"/>'s own summary uses to walk from saturation to a
    /// <c>TimeoutExceeded</c>.
    /// </summary>
    private const decimal CurveStep = 0.025m;

    /// <summary>
    /// Twice capacity. Past the cap the branch is a straight line and drawing more of it teaches
    /// nothing; stopping before 1,725 would cut off the only point on the curve that reaches a
    /// timeout.
    /// </summary>
    private const decimal CurveEnd = 2m;

    /// <summary>
    /// Constant for the life of the process, because the model is. Built once rather than per
    /// request — there is nothing in it that a request could vary.
    /// </summary>
    public static ModelReferenceDto Snapshot { get; } = Build();

    private static ModelReferenceDto Build() => new(
        Enum.GetValues<NodeType>()
            .Select(type => new ComponentModelDto(type, CapacityModel.PerInstance(type), LatencyModel.Processing(type)))
            .ToList(),
        Enum.GetValues<ProtocolType>()
            .Select(protocol => new NetworkHopDto(protocol, LatencyModel.Hop(protocol)))
            .ToList(),
        CapacityModel.SaturationWarning,
        LatencyModel.UnboundedWait,
        CapacityModel.DefaultHitRatio,
        CapacityModel.DefaultCdnHitRatio,
        BuildQueueing());

    private static QueueingCurveDto BuildQueueing()
    {
        var points = new List<QueueingPointDto>();

        for (var utilization = 0m; utilization <= CurveEnd; utilization += CurveStep)
            points.Add(new QueueingPointDto(utilization, CapacityModel.QueueingFactor(utilization)));

        // The cap is asked of the function instead of being written down as 20, and ρ = 1 is where
        // it can be: both branches meet there at ×20, which is the continuity DT.6 was built for.
        return new QueueingCurveDto(CapacityModel.QueueingFactor(1m), points);
    }
}

/// <summary>
/// What <c>GET /api/model</c> answers. Read-only by construction: there is no request body, no id
/// and nothing to persist — the model is a criterion, not something the user fills in
/// (<see cref="LatencyModel"/>'s own words).
/// </summary>
/// <param name="SaturationWarning">DT.5. At or above this share of its capacity a component is Degraded — never worse.</param>
/// <param name="UnboundedWaitMs">D4.2. What a call with no timeout takes to give up, in milliseconds.</param>
/// <param name="DefaultHitRatio">DT.3. What a cache absorbs when nobody says otherwise.</param>
/// <param name="DefaultCdnHitRatio">QA §3.2. Higher than a cache's on purpose, and the gap is the lesson.</param>
public sealed record ModelReferenceDto(
    IReadOnlyList<ComponentModelDto> Components,
    IReadOnlyList<NetworkHopDto> Network,
    decimal SaturationWarning,
    int UnboundedWaitMs,
    decimal DefaultHitRatio,
    decimal DefaultCdnHitRatio,
    QueueingCurveDto Queueing);

/// <summary>
/// One row of the table: what one instance of this type takes, and what it costs to pass through
/// it. The levers that multiply the first (<c>instances</c>, <c>replicas</c>, <c>readReplicas</c>)
/// are a property of the node the user configures, not of the type, so they are not here.
/// </summary>
public sealed record ComponentModelDto(NodeType Type, int CapacityPerInstance, int ProcessingMs);

/// <summary>The hop itself, per protocol — what the wire costs before anybody's code runs.</summary>
public sealed record NetworkHopDto(ProtocolType Protocol, int Ms);

/// <summary>
/// DT.6, sampled. The formula is two branches and hand-computable on purpose, and it is what
/// composes the two axes: a component over its capacity does not fail, it slows down, and the call
/// against it eventually loses to a timeout.
/// </summary>
public sealed record QueueingCurveDto(decimal Cap, IReadOnlyList<QueueingPointDto> Points);

public sealed record QueueingPointDto(decimal Utilization, decimal Factor);
