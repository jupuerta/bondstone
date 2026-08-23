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
    /// V14 B3. Components that <strong>decide something</strong>, as opposed to components you
    /// <strong>pass through</strong> — the distinction <c>SYNC_CHAIN</c> measures its chains in.
    ///
    /// <strong>The threshold was calibrated on a palette of eleven and a sentence of three.</strong>
    /// V1 set "3 warns, 5 is critical" against <c>API → Orders → Payments</c>: three places where
    /// business logic runs, three deploys, three teams, three things that can be wrong on their own.
    /// With fifteen types the same count also sums the edge cache, the router, the read cache and
    /// the store, so <c>Client → Cdn → Gateway → Api → Service → Cache → Db</c> — the canonical read
    /// path of every layered web application, protected on every hop — came out a <em>Critical</em>
    /// advising a queue in the middle of a read. The chain had not grown; the palette had.
    ///
    /// A <see cref="NodeType.Cdn"/>, a <see cref="NodeType.Gateway"/>, a
    /// <see cref="NodeType.LoadBalancer"/>, a <see cref="NodeType.Cache"/> or a
    /// <see cref="NodeType.Database"/> on the path is the layer the call crosses, not another link
    /// that fails on its own in the sense this rule grades. What counts is what runs logic — yours
    /// (<see cref="NodeType.Api"/>, <see cref="NodeType.Service"/>, <see cref="NodeType.Worker"/>)
    /// or somebody else's (<see cref="NodeType.ExternalService"/>,
    /// <see cref="NodeType.IdentityProvider"/>, which is a hop out of your process however you draw
    /// it).
    ///
    /// <see cref="NodeType.Queue"/> is absent because it can never be in a synchronous chain
    /// anyway, and <see cref="NodeType.Scheduler"/> and <see cref="NodeType.Client"/> because they
    /// are where a flow <em>starts</em> - an origin is not a link.
    /// </summary>
    public static bool IsLogicComponent(NodeType type) =>
        type is NodeType.Api or NodeType.Service or NodeType.Worker
             or NodeType.ExternalService or NodeType.IdentityProvider;

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
    ///
    /// V14 B1. SPOF's reachability walk reads it too, and that was the one place the doctrine was
    /// written down twice: the counting side skipped the telemetry while the walk followed it, so
    /// instrumenting a graph made its front door stop being a point of passage.
    /// </summary>
    public static bool IsTelemetry(NodeType type) => type is NodeType.Observability;

    /// <summary>
    /// An edge with the telemetry at either end: instrumentation, not a call in the architecture's
    /// sense.
    ///
    /// <strong>V14 B9. Every structural rule reads this, and there is exactly one deliberate
    /// exception.</strong> <c>HIGH_COUPLING</c>, <c>MISSING_CACHE</c>, <c>QUEUE_NO_CONSUMER</c> and
    /// <see cref="Simulation.LoadModel"/> already did; <c>CYCLE</c>, <c>SYNC_CHAIN</c>,
    /// <c>PROTOCOL_MISMATCH</c> and both halves of <see cref="SpofRule"/> now do too. The doctrine
    /// is one sentence: <em>wiring the telemetry costs a point nowhere except on the edge's own
    /// resilience.</em>
    ///
    /// <strong>And that exception is <c>MISSING_RESILIENCE</c>, which is not an oversight.</strong>
    /// A blocking call to your metrics backend on the request path is a real finding - it is the one
    /// thing about an instrumentation edge that is genuinely the architect's, and the one the
    /// student can act on by making the hop asynchronous, which is what the guided fix already
    /// draws. Adding this predicate there would silence the only true thing the analyzer says about
    /// these edges. If a later reading is tempted to "finish the job", this is the job being
    /// finished.
    /// </summary>
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
