using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V13 D1.3. What a component <em>is</em>, in the two or three distinctions the rules actually
/// argue from — kept in one place because three rules now ask, and three slightly different
/// answers to "is this a database-ish thing" is how a type stops meaning anything.
///
/// <strong>This is V2 D3.2 being paid rather than restated.</strong> That decision says a type earns
/// its place only if some rule looks at it, and until V13 three of the eleven were furniture:
/// <see cref="NodeType.Service"/>, <see cref="NodeType.Worker"/> and
/// <see cref="NodeType.ObjectStorage"/> were drawn, saved, exported and simulated without a single
/// rule ever reading them. They are read here, and through here by SPOF, HIGH_COUPLING and CYCLE.
///
/// The predicates are about <em>behaviour under failure</em>, never about the palette's categories:
/// what makes ObjectStorage sit next to Database is not that both hold bytes, it is that a fallback
/// in front of either one buys you degraded reads and never a write.
/// </summary>
public static class NodeRoles
{
    /// <summary>
    /// Components that are the truth. Losing one loses writes, and <strong>no fallback restores a
    /// write</strong> — it can only serve something stale or refuse. This is the asymmetry that
    /// keeps a fallback-protected <see cref="NodeType.Database"/> a single point of failure while
    /// the same wiring in front of a <see cref="NodeType.Cache"/> is simply a cache doing its job
    /// (D1.1).
    /// </summary>
    public static bool IsSystemOfRecord(NodeType type) =>
        type is NodeType.Database or NodeType.ObjectStorage;

    /// <summary>
    /// Components whose whole purpose is to sit in front of several others.
    /// <strong>Fan-out is their function</strong>, so what a finding has to say to one of them is a
    /// different sentence from what it says to a service that grew seven dependencies (D1.2).
    /// </summary>
    public static bool IsRouter(NodeType type) =>
        type is NodeType.Gateway or NodeType.LoadBalancer;

    /// <summary>
    /// Where state lives. <see cref="NodeType.Queue"/> is deliberately not here: a queue inside a
    /// cycle is the mechanism that breaks it, not the state two components are fighting over
    /// (D1.2, CYCLE).
    /// </summary>
    public static bool IsDataStore(NodeType type) =>
        IsSystemOfRecord(type) || type is NodeType.Cache;

    /// <summary>
    /// QA §3.1. The outside itself, drawn as a box. Its edges are the requests, not calls anybody
    /// configures - so the resilience rules skip them, SPOF neither grades it nor counts it as a
    /// dependent that falls, and <see cref="EntryPoints"/> treats it as <em>the</em> declaration
    /// of where public traffic comes from.
    /// </summary>
    public static bool IsTrafficOrigin(NodeType type) => type is NodeType.Client;

    /// <summary>
    /// QA §3.1. Whether this edge is the outside calling in. The request itself is not a call
    /// anybody configures - you do not put a timeout on somebody's browser - so the resilience
    /// rules (MISSING_RESILIENCE, SYNC_CHAIN, TIMEOUT_TOO_SHORT, RETRY_AMPLIFICATION,
    /// EXTERNAL_NO_FALLBACK) all skip these edges through this one predicate, and drawing a
    /// Client never opens a diagram with advice about a knob that is not the architect's.
    /// </summary>
    public static bool IsRequestFromOutside(ArchitectureGraph graph, Connection edge) =>
        graph.NodesById.TryGetValue(edge.SourceNodeId, out var source) && IsTrafficOrigin(source.Type);

    /// <summary>
    /// QA B6. Types whose outgoing edges are work they start, never traffic somebody sent from
    /// outside: a Worker polling its queue, a clock firing jobs, telemetry reading what it watches.
    /// A root of one of these is not a door - which is what stopped a background Worker from
    /// "exposing" its own database, and what keeps losing one from counting as losing requests.
    /// </summary>
    public static bool ActsFromInside(NodeType type) =>
        type is NodeType.Worker or NodeType.Observability or NodeType.Scheduler;

    /// <summary>
    /// QA §4 (coverage). The telemetry box itself: the one component that observes and is never
    /// observed, and the one whose edges are not dependencies in any sense the structural rules
    /// care about - you do not <em>depend</em> on your metrics sink, and it does not <em>consume</em>
    /// from the queue it watches. SPOF already kept it out of its pattern on both sides (QA B1);
    /// this is that doctrine made into a predicate so HIGH_COUPLING, MISSING_CACHE and
    /// QUEUE_NO_CONSUMER read the same one, and wiring telemetry never costs a point anywhere but
    /// on the edge's own resilience, which is real.
    /// </summary>
    public static bool IsTelemetry(NodeType type) => type is NodeType.Observability;

    /// <summary>An edge with the telemetry at either end: instrumentation, not a call in the architecture's sense.</summary>
    public static bool IsTelemetryEdge(ArchitectureGraph graph, Connection edge) =>
        (graph.NodesById.TryGetValue(edge.SourceNodeId, out var source) && IsTelemetry(source.Type))
        || (graph.NodesById.TryGetValue(edge.TargetNodeId, out var target) && IsTelemetry(target.Type));

    /// <summary>
    /// QA §4 (coverage). What NO_OBSERVABILITY counts as yours to watch. Not the telemetry itself,
    /// and not the boxes that are somebody else's to instrument: the outside
    /// (<see cref="IsTrafficOrigin"/>), a third party's service, a CDN - the same three the
    /// analyzer already declines to size or to replicate, for the same reason (V8 §6.2, QA §3.2).
    /// </summary>
    public static bool IsObservable(NodeType type) =>
        !IsTelemetry(type)
        && !IsTrafficOrigin(type)
        && type is not (NodeType.ExternalService or NodeType.Cdn);
}
