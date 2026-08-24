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
    /// V14 B8. What can drain a queue: the types that <strong>run code on a message</strong>. A
    /// <see cref="NodeType.Worker"/> is the one the product recommends; a
    /// <see cref="NodeType.Service"/> or an <see cref="NodeType.Api"/> subscribing is ordinary; an
    /// <see cref="NodeType.ExternalService"/> is somebody else's code, and it still runs. Nothing
    /// else does: a database does not process a message, a cache does not, a balancer does not, and
    /// the outside (<see cref="NodeType.Client"/>) least of all. Until V14 <c>QUEUE_NO_CONSUMER</c>
    /// accepted any arrow leaving the queue as the delivery, so <c>Api → Cola → Db</c> - the exact
    /// shape the rule exists to catch, work that goes in and never comes out - got the green tick,
    /// and <c>QUEUE_NO_DLQ</c> then handed out the second-level advice over the missing first.
    ///
    /// Narrower than <see cref="IsLogicComponent"/> on purpose: an
    /// <see cref="NodeType.IdentityProvider"/> is a hop out of your process for <c>SYNC_CHAIN</c>,
    /// but nobody subscribes it to a queue.
    /// </summary>
    public static bool ProcessesMessages(NodeType type) =>
        type is NodeType.Worker or NodeType.Service or NodeType.Api or NodeType.ExternalService;

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
    /// V14 B10. The other end of the same predicate: a call <em>made by</em> somebody else's
    /// service is not one this architect can contain either. <c>Api → Stripe</c> is yours - the
    /// timeout, the fallback, the retries all sit on your side of it. <c>Stripe → Maps</c> is
    /// Stripe's: drawing it says "the provider depends on another provider", which is worth knowing
    /// and is nothing you can put a knob on. Every rule that asked the user to configure it was
    /// handing out an instruction nobody can follow, the exact failure <c>UNDERSIZED_COMPONENT</c>
    /// refuses to commit on the same type (V8 §6.2, QA V14 B2).
    /// </summary>
    public static bool IsCallByThirdParty(ArchitectureGraph graph, Connection edge) =>
        graph.NodesById.TryGetValue(edge.SourceNodeId, out var source) && source.Type == NodeType.ExternalService;

    /// <summary>
    /// V14 B10. <strong>The one question the five resilience rules ask about an edge before they
    /// read it</strong>: is this a call the architect configures? The request from the outside is
    /// not (QA §3.1), and a third party's own call is not (B10) - two ends, one predicate, because
    /// until V14 the rules read one and not the other, and <c>EXTERNAL_NO_FALLBACK</c>,
    /// <c>MISSING_RESILIENCE</c>, <c>TIMEOUT_TOO_SHORT</c>, <c>RETRY_AMPLIFICATION</c> and
    /// <c>SYNC_CHAIN</c> all opined on Stripe's timeout to Google Maps.
    /// </summary>
    public static bool IsNotYourCall(ArchitectureGraph graph, Connection edge) =>
        IsRequestFromOutside(graph, edge) || IsCallByThirdParty(graph, edge);

    /// <summary>
    /// QA B6. Types whose outgoing edges are work they start, never traffic somebody sent from
    /// outside: a Worker polling its queue, a clock firing jobs, telemetry reading what it watches.
    /// A root of one of these is not a door - which is what stopped a background Worker from
    /// "exposing" its own database, and what keeps losing one from counting as losing requests.
    /// </summary>
    public static bool ActsFromInside(NodeType type) =>
        type is NodeType.Worker or NodeType.Observability or NodeType.Scheduler;

    /// <summary>
    /// V14 B6. The inference turned the right way up. QA B6 excluded three types from "a root is a
    /// door" and left the other twelve in, so a queue, a cache, a service or a database drawn at the
    /// top of the flow - the ordinary shape of a diagram of <em>part</em> of a system - was the
    /// internet, and the data behind it was a breach. <strong>A root is a door only if its type is
    /// something the outside can reach</strong>: the edge, the balancer, the router, the Api, or a
    /// third party calling in (a webhook is outside traffic, which is why ExternalService stayed
    /// an origin through QA B6). Everything else at the root is a drawing that starts in the
    /// middle, not a hole in the wall. A drawn <see cref="NodeType.Client"/> still settles the
    /// question before this is ever asked (<see cref="EntryPoints.TrafficOrigins"/>).
    /// </summary>
    public static bool ReceivesFromOutside(NodeType type) =>
        type is NodeType.Api or NodeType.Gateway or NodeType.LoadBalancer
             or NodeType.Cdn or NodeType.ExternalService;

    /// <summary>
    /// V14 B6 (N12). Where the outside stops being the outside: the components that can ask who is
    /// calling, which is the same list as the ones that carry the <c>authentication</c> field.
    /// Until V14 that was Api and Gateway, so <c>Client → Bff(Service) → Db</c> - an ordinary BFF -
    /// walked straight through the service and found the database "reachable without passing
    /// through any API", with advice to rename the box. A Service facing the outside is a door
    /// like any other; the right finding is the question every door gets, not a shape error.
    /// </summary>
    public static bool CanAuthenticate(NodeType type) =>
        type is NodeType.Api or NodeType.Gateway or NodeType.Service;

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
    /// <strong>And that exception is <c>MISSING_RESILIENCE</c>, which is not an oversight - but it
    /// is half an edge wide, not a whole one (V15 A3).</strong> A blocking call to your metrics
    /// backend on the request path is a real finding - it is the one thing about an instrumentation
    /// edge that is genuinely the architect's, and the one the student can act on by making the hop
    /// asynchronous, which is what the guided fix draws. That call is <see cref="IsTelemetryPush"/>:
    /// <c>Api → Obs</c>. The other direction, <see cref="IsTelemetryScrape"/>, is the telemetry
    /// reading what it watches - on no request path, with no knob on it that is yours - and until
    /// V15 the rule read both the same way, so wiring the telemetry as the canvas draws it
    /// (synchronous, unconfigured) cost Resilience a Warning per box and, towards the front door,
    /// a Critical whose explanation said the Api's fall "propagates straight back" - into the
    /// metrics collector. A push is graded on its timeout and never escalated on a SPOF; a scrape
    /// is skipped here like everywhere else. If a later reading is tempted to "finish the job" on
    /// the push as well, this is the job being finished: silencing it would silence the only true
    /// thing the analyzer says about these edges.
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
    /// V14 B12 (observación). The IdentityProvider joins the third group: it is somebody else's
    /// service - the same reason SPOF's suggestion refuses to replicate it and CapacityModel hands
    /// it a provider's capacity - so you neither wire Auth0 to your telemetry nor give it
    /// instances. Through <see cref="IsYoursToSize"/> this also stops UNDERSIZED_COMPONENT from
    /// telling anybody to scale their identity provider.
    public static bool IsObservable(NodeType type) =>
        !IsTelemetry(type)
        && !IsTrafficOrigin(type)
        && type is not (NodeType.ExternalService or NodeType.Cdn or NodeType.IdentityProvider);

    /// <summary>
    /// V14 B2. What <c>UNDERSIZED_COMPONENT</c> may tell you to grow. Every sentence that rule
    /// knows - add instances, add replicas, share the work - is an instruction, and the five types
    /// left out here are the ones where that instruction is impossible to follow:
    /// <see cref="NodeType.ExternalService"/> is somebody else's to scale (V8 §6.2), the
    /// <see cref="NodeType.Client"/> is your users, a <see cref="NodeType.Cdn"/> is a provider's
    /// edge, a <see cref="NodeType.Scheduler"/> is a clock, and the telemetry box is nobody's lesson.
    /// They are exactly the types <see cref="Simulation.CapacityModel"/> hands an enormous capacity
    /// <em>so that they never saturate</em> - and a declared load big enough got past that number
    /// and produced "give your users more instances". The same criterion as
    /// <see cref="IsObservable"/>, widened by the two boxes that do not take traffic at all.
    /// </summary>
    public static bool IsYoursToSize(NodeType type) =>
        IsObservable(type) && type is not NodeType.Scheduler;

    /// <summary>
    /// V15 C2. The provider's edge: the one component whose redundancy is neither drawn nor
    /// declared, because it is the provider's product - an anycast edge over more points of
    /// presence than this canvas has boxes. The analyzer already declines to size it
    /// (<see cref="IsYoursToSize"/>) and to ask for its telemetry (<see cref="IsObservable"/>), and
    /// <see cref="Simulation.CapacityModel"/> hands it a capacity it never reaches, all for the
    /// reason written there: a saturated CDN is nobody's lesson. <c>SPOF</c> was the one rule still
    /// grading it - a Critical whose own suggestion opened with "you can neither replicate it nor
    /// fix it", cleared only by two CDNs behind a balancer, while <c>MISSING_CDN</c> asked for the
    /// very box one area over, so following that advice cost fifteen points of Availability and the
    /// best architecture drawable topped out at 96.
    ///
    /// Narrower than "somebody else's" on purpose: a third party's service and the identity
    /// provider stay graded, because the call <em>to</em> them is the architect's and a fallback on
    /// it is the way out their cards name. The call into the CDN is the outside's, so that way out
    /// was never available for this type. Not graded, and not exempt either: what it fronts is
    /// measured through it as through anything else.
    /// </summary>
    public static bool IsProviderEdge(NodeType type) => type is NodeType.Cdn;

    /// <summary>
    /// V15 B2. <strong>What one box is a copy of, as far as the model can tell.</strong> Two
    /// counting rules - <c>SHARED_DATABASE</c> and <c>MISSING_CACHE</c> - ask "how many services
    /// (consumers) meet on this store", and until V15 they counted boxes, so
    /// <c>LB → Pedidos1, Pedidos2 → Db</c> was "'Db' es el almacén compartido de 2 servicios" with
    /// advice to give each its own data - which splits one service's database in two. V14 B11 had
    /// retired the "equivalent sibling" inference because it guessed wrong, and it was also the
    /// only thing that knew two boxes were copies.
    ///
    /// The one kind of copy the drawing can state today is the one <see cref="SpofRule"/> already
    /// reads for its balancer exemption: <strong>boxes of the same type fanned out from the same
    /// <see cref="NodeType.LoadBalancer"/></strong>. The set is keyed by that balancer and that
    /// type; a box nothing balances is a set of its own. Two, like SpofRule's
    /// <c>RedundantInstancesMin</c>: a balancer over a single box balances nothing. A box behind
    /// two balancers belongs to the first drawn.
    ///
    /// <strong><see cref="NodeType.LoadBalancer"/> alone, never <see cref="IsRouter"/>.</strong> A
    /// gateway routes to <em>different</em> things - the services of <c>gateway-microservices</c>
    /// and <c>worst-case</c> hang from one - and reading them as copies would erase the
    /// integration-by-database lesson from both references and from the product.
    ///
    /// It is an approximation, and a known one (V14 B11, T14): two different services drawn behind
    /// one balancer read as copies. The successor is a declared <c>replicaOf</c> / replica group on
    /// the node, so redundancy is stated the way <c>replicas</c> and <c>instances</c> already are;
    /// until the schema carries it, this is the drawing's only way of saying "these are the same
    /// thing".
    /// </summary>
    public static (Guid Anchor, NodeType Type) ReplicaSetOf(ArchitectureGraph graph, Node node)
    {
        var balancer = graph.IncomingByTarget[node.Id]
            .Select(edge => graph.NodesById.GetValueOrDefault(edge.SourceNodeId))
            .Where(source => source is { Type: NodeType.LoadBalancer })
            .Where(source => graph.OutgoingBySource[source!.Id]
                .Select(edge => graph.NodesById.GetValueOrDefault(edge.TargetNodeId))
                .Where(target => target?.Type == node.Type)
                .Select(target => target!.Id)
                .Distinct()
                .Count() >= 2)
            .OrderBy(source => source!.Id)
            .FirstOrDefault();

        return (balancer?.Id ?? node.Id, node.Type);
    }

    /// <summary>
    /// V15 A3. The telemetry reading what it watches: <c>Obs → Api</c>. It starts at the metrics
    /// sink and sits on no request path, so nothing about it is a call the architect configures -
    /// a timeout on Prometheus's poll of your Api is Prometheus's, and a fallback on it means
    /// nothing. It is the direction a student draws by hand, and the canvas makes every new arrow
    /// synchronous and unconfigured, which is how following NO_OBSERVABILITY's advice used to
    /// cost a <c>MISSING_RESILIENCE</c> Warning per box. The one half of
    /// <see cref="IsTelemetryEdge"/> that the resilience of the edge is not about.
    /// </summary>
    public static bool IsTelemetryScrape(ArchitectureGraph graph, Connection edge) =>
        graph.NodesById.TryGetValue(edge.SourceNodeId, out var source) && IsTelemetry(source.Type);

    /// <summary>
    /// V15 A3. A component sending its own telemetry: <c>Api → Obs</c>, the other half. Drawn
    /// synchronous it is a blocking call to the metrics backend on the request path - the one
    /// thing about an instrumentation edge that is genuinely the architect's, and the reason
    /// <see cref="IsTelemetryEdge"/> keeps <c>MISSING_RESILIENCE</c> as its deliberate exception.
    /// What the student can do about it is make the hop asynchronous: it is what the guided fix
    /// of NO_OBSERVABILITY already draws, and what this edge's own fix now offers. Two telemetry
    /// boxes wired to each other are neither (QA B5): that is not instrumentation.
    /// </summary>
    public static bool IsTelemetryPush(ArchitectureGraph graph, Connection edge) =>
        !IsTelemetryScrape(graph, edge)
        && graph.NodesById.TryGetValue(edge.TargetNodeId, out var target) && IsTelemetry(target.Type);

    /// <summary>
    /// V15 E5. What the outside must never reach without a door in the way: yours, takes traffic,
    /// and cannot ask who is calling. <see cref="IsYoursToSize"/> is the first two - not the
    /// outside, not the telemetry, not a provider's box, not a clock - and the third removes the
    /// doors themselves (<see cref="CanAuthenticate"/>), which get the door's question instead,
    /// and the balancer, whose whole function is to be reached and hand the request to one. What
    /// is left is a Worker, a Cache, a Queue, a Topic and the two systems of record: the boxes
    /// with no field to declare anything with and nothing to do about a request but take it.
    ///
    /// The <see cref="NodeType.Cdn"/> is out for the balancer's reason, and the
    /// <see cref="NodeType.Scheduler"/> because it does not take a request at all: an arrow from
    /// the users into a clock is a drawing that means nothing, not a hole in the wall. The stores
    /// are <em>in</em> even though <c>EXPOSED_DATABASE</c> is the rule that speaks for them
    /// (<see cref="EntryPoints.Unguarded"/> leaves them to it): this predicate is about what is
    /// true of a type, and which rule says it is the rule's decision.
    /// </summary>
    public static bool MustSitBehindADoor(NodeType type) =>
        IsYoursToSize(type) && !CanAuthenticate(type) && type is not NodeType.LoadBalancer;
}
