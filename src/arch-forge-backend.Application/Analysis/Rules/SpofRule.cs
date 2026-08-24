using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V2 D4.x. Two patterns, because with the invariant "source depends on target" (D3.3) a gateway
/// has zero dependents: defining a SPOF only as "≥2 components depend on it" would never have
/// fired on the product's flagship example.
///
/// V13 D1.1. Pattern A stopped counting edges and started <strong>reading</strong> them. Until V13
/// this rule never touched <see cref="Connection.Synchronous"/> nor
/// <see cref="ArchitectureGraph.ResilienceOf"/>, so an edge that hands work to a queue and an edge
/// that blocks on a database were the same arrow to it — and <c>resilient-async</c>, the fixture
/// this product ships as the example of doing it right, came out with three Warnings on components
/// that were protected on every single way in.
///
/// V13 D4.1. And its explanation stopped being a constant. <c>SPOF</c> could say <em>how many</em>
/// components depended on something and never <em>how much</em> of the service went with it - so the
/// worst finding the analyzer emits read as an inventory. It now asks
/// <see cref="OutageImpact.OfLosing"/>, which is the same walk the impact panel prints, and says the
/// figure out loud.
///
/// QA B1 (docs/QA-analisis.md, scoring v6). Pattern B's <em>severity</em> now comes from that same
/// measurement instead of being a constant Critical. The walk itself still reads every edge -
/// passage is about traffic flowing, and a fallback dies with the caller that holds it, so a
/// gateway whose every way out is covered is still the door (see
/// <c>EveryWayInBeingSoft_DoesNotStopPatternB</c>) - but a passage point whose loss fails no
/// request, a queue in the middle of the asynchronous pipeline this product recommends, is a
/// Warning whose card and severity finally say the same thing. Observability stays out of the
/// pattern on both sides, for the reason <see cref="Simulation.CapacityModel"/> already wrote
/// down: the telemetry box is nobody's lesson, and counting it let the act of wiring telemetry
/// turn a clean entry point into a SPOF.
///
/// V14 B1 (docs/V14/QA-analisis-V14.md). And the half of that fix that was missed: the counting
/// side skipped the telemetry, <see cref="ReachableFromRoots"/> did not. Wiring
/// <c>Obs → Api</c>, <c>Obs → Svc</c>, <c>Obs → Db</c> made <c>Obs</c> the root of the graph and
/// let it reach everything behind the Api through its own scrape cables, so the Api stopped being
/// a point of passage and its Critical vanished - the reverse of the V13 bug and worse than it,
/// because that one added noise and this one <em>hid a real finding</em>, at the exact moment the
/// student followed <c>NO_OBSERVABILITY</c>'s advice. A rule cannot hold two notions of "this is
/// not traffic" depending on which half of itself you read; the walk holds the only one.
///
/// V14 B4. <strong>Redundancy is something you can say, on every type that has a word for it.</strong>
/// QA B2 wrote the doctrine - a system of record's redundancy is declared, never inferred - and it
/// was applied to exactly one type. For the other fourteen the card asked for a second instance, the
/// inspector edited <c>instances</c>, the node printed "10 inst" on its own face, the load model
/// multiplied capacity by it, and this rule was the only place in the product where the field meant
/// nothing: an Api with ten instances was told, in a Critical, to add a second one. Both patterns now
/// stop at <see cref="NodeSettings.HasDeclaredInstances"/> wherever the field exists, so the loop
/// advice → field → green closes; where it does not exist, the topological way out the component's
/// own suggestion names is still the way out.
///
/// V14 B11. And with a way to <em>say</em> two copies, the guess at them is gone: the "equivalent
/// sibling" exemption - same type, same consumers, therefore replicas - exempted Users and Orders
/// from each other for sharing a gateway and an Api. Redundancy is drawn or declared, never inferred
/// from who calls you. V14 B12: the identity provider is a SPOF with one dependent, graded by what
/// losing it costs, because one door is every request that door admits.
///
/// V15 B1 (docs/V15/QA-analisis-V15-TODO.md). And B4 had reopened the loop the balancer's
/// exemption exists to close. <see cref="IsRedundancyMechanism"/> recognised only the drawn way of
/// saying "there are two behind it", so <c>LB → Api(instances: 2)</c> was a Critical on the
/// balancer - fifteen points of Availability under the same system drawn as
/// <c>LB → Api + Api2</c> - with a card asking for a second instance of a type that has no such
/// field, and a balancer in front of the balancer as the only way out. <see cref="BalancedTypes"/>
/// now reads both ways of saying it. The third way, inference, is still gone (B11).
///
/// V15 C2. <strong>A CDN is not graded as a single point of failure.</strong> It was a Critical
/// that only two CDNs behind a balancer could clear, while <c>MISSING_CDN</c> asked for one: adding
/// the edge that rule asks for took Availability from 100 to 85, and the best architecture drawable
/// topped out at 96 because of it. It is the provider's edge
/// (<see cref="NodeRoles.IsProviderEdge"/>): the analyzer already declines to size it and to ask for
/// its telemetry, and <see cref="Simulation.CapacityModel"/> hands it a capacity it never reaches,
/// all for the one reason written there - it is nobody's lesson. Its redundancy is the provider's
/// product, and nothing on this canvas can declare or wire it, which is B4's doctrine broken for one
/// type. Unlike a third party's service or the identity provider, which stay graded because the
/// call to them is yours to cover, the call into the CDN is the outside's. What it fronts is still
/// graded: the walk passes through it as through anything else.
/// </summary>
public class SpofRule : IArchitectureRule
{
    private const int SharedDependencyMin = 2;
    private const int SharedDependencyCritical = 3;
    private const int UnreachableMin = 2;
    private const int RedundantInstancesMin = 2;

    public string Code => RuleCodes.Spof;
    public Area Area => Area.Availability;
    public Severity DefaultSeverity => Severity.Critical;
    public RulePhase Phase => RulePhase.Producer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.SPOF.passed.message");

    public LocalizedText PassedExplanation => LocalizedText.Of("finding.SPOF.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        // Two counts, because they answer two different questions (D1.1). "Who points at it" is
        // what the redundancy checks compare - two replicas are equivalent when they serve the same
        // consumers, however those consumers call them. "Who falls with it" is what pattern A
        // grades, and a consumer holding a fallback does not fall.
        // A Client is not a dependent (QA §3.1): its edges are the traffic itself, and counting it
        // would both inflate pattern A ("the users depend on the Api") and let two different Apis
        // exempt each other as "replicas" for serving the same crowd. And a node is never its own
        // dependent (QA B7): a self-loop is CYCLE's finding, and counting it here turned one real
        // consumer into the two the threshold asks for.
        // V14 B9, and the last half of B1. The walk stopped reading instrumentation; this side of
        // the rule had not, and it is the side a student actually hits - the canvas draws a new
        // edge synchronous by default, so an `Api → Obs` wired by hand made the telemetry box a
        // Critical SPOF that four components "depended on", and a scrape drawn the same way turned
        // the database's Warning into a Critical by being its third dependent. Nobody depends on
        // their metrics sink: losing it loses the graphs, not the request.
        var dependentsByNode = graph.Nodes.ToDictionary(
            node => node.Id,
            node => graph.IncomingByTarget[node.Id]
                .Where(edge => edge.SourceNodeId != node.Id
                               && !NodeRoles.IsRequestFromOutside(graph, edge)
                               && !NodeRoles.IsTelemetryEdge(graph, edge))
                .Select(c => c.SourceNodeId)
                .Distinct()
                .ToHashSet());

        var hardDependentsByNode = graph.Nodes.ToDictionary(
            node => node.Id,
            node => graph.IncomingByTarget[node.Id]
                .Where(edge => edge.SourceNodeId != node.Id
                               && !NodeRoles.IsRequestFromOutside(graph, edge)
                               && !NodeRoles.IsTelemetryEdge(graph, edge)
                               && IsHardDependency(graph, edge))
                .Select(edge => edge.SourceNodeId)
                .Distinct()
                .ToHashSet());

        // Nothing to check while nobody depends on anybody. Deliberately measured on the arrows and
        // not on the hard ones: an architecture wired entirely with queues has been checked and
        // came out clean, which is a passed check and not silence (D1.4).
        if (!dependentsByNode.Values.Any(dependents => dependents.Count > 0))
            return RuleResult.NotApplicable;

        var reachableFromRoots = ReachableFromRoots(graph, excluded: null);

        // Once for the whole rule, not once per finding: it is the same graph every time, and
        // building it inside the loop would be O(V) copies of an answer that cannot change (D4.1).
        var load = LoadModel.Of(graph);
        var findings = new List<RuleFinding>();

        foreach (var node in graph.Nodes)
        {
            // The outside is not a component of yours (QA §3.1): you cannot replicate the
            // internet, and "the users are a single point of failure" is not a sentence.
            if (NodeRoles.IsTrafficOrigin(node.Type))
                continue;

            // V15 C2. Nor is the provider's edge. Its redundancy is not something this drawing can
            // say, so grading it turned MISSING_CDN's advice into a Critical on arrival, cleared
            // only by two CDNs behind a balancer. Skipped here and nowhere else: the walk still
            // passes through it, and what it fronts is measured exactly as before.
            if (NodeRoles.IsProviderEdge(node.Type))
                continue;

            if (IsRedundancyMechanism(graph, node) || HasRedundancy(graph, node))
                continue;

            var dependents = hardDependentsByNode[node.Id].Count;

            // Pattern B: how much of the system stops being reachable without it. The walk reads
            // every edge that carries traffic - passage is about traffic flowing, and a fallback
            // dies with the caller that holds it - and only those (V14 B1). Observability stays out
            // of the pattern on both sides, as it has since QA B1: it is never a point of passage
            // for requests and it never counts among what falls, for the same reason CapacityModel
            // gives it an enormous capacity - the telemetry box is nobody's lesson. That used to be
            // spelled out here as two extra conditions while ReachableFromRoots walked the
            // instrumentation anyway; it is one rule now, stated once, in the walk.
            var unreachable = reachableFromRoots.Count == 0
                ? 0
                : reachableFromRoots.Except(ReachableFromRoots(graph, excluded: node.Id))
                    .Count(id => id != node.Id
                                 && graph.NodesById.TryGetValue(id, out var lost)
                                 && !NodeRoles.IsTrafficOrigin(lost.Type));

            var passage = unreachable >= UnreachableMin;
            var shared = dependents >= SharedDependencyMin;

            // V14 B12. The identity provider is the one type for which a single hard dependent is
            // enough, and it is the exception the type was added to earn: "on the synchronous
            // path of every request it is the most famous SPOF in the industry". Pattern A asked
            // for two dependents and a door has one IdP; pattern B saw nothing behind it; and the
            // analyzer said nothing at all about Auth0 while handing the drawing Resilience 100.
            // One door blocking on it without a fallback is every request that door admits.
            var identity = node.Type == NodeType.IdentityProvider && dependents >= 1;

            if (!passage && !shared && !identity)
                continue;

            // QA B1. Pattern B used to be "always Critical", and that severity contradicted its
            // own explanation: a queue in the middle of an asynchronous pipeline is a genuine
            // point of passage whose loss delays work and fails no request, and it came out as a
            // Critical whose card said not one request is lost. The severity now comes from the
            // same measurement the explanation prints - losing real traffic is Critical, a
            // passage whose callers are all covered is a Warning - so the two halves of the
            // finding can no longer disagree.
            var affected = OutageImpact.OfLosing(graph, load, node.Id);
            var passageSeverity = affected > 0m ? Severity.Critical : Severity.Warning;

            // The identity provider's severity is the measurement too (B12), not the count: one
            // dependent is not "a small blast radius" when that dependent is the front door and
            // every request it admits authenticates against this. The same reading pattern B
            // uses - losing real traffic is Critical, a covered caller is a Warning - so the card
            // that says "100 % of requests" is never a Warning.
            var sharedSeverity = identity
                ? passageSeverity
                : dependents >= SharedDependencyCritical ? Severity.Critical : Severity.Warning;

            // One finding per node with the highest severity it earns (D4.x), said by the pattern
            // that earns it - and by pattern B on a tie, which is who always spoke when both fired.
            var byPassage = passage && (!shared || passageSeverity >= sharedSeverity);

            findings.Add(Report(graph, node, byPassage ? passageSeverity : sharedSeverity, affected,
                byPassage
                    ? LocalizedText.Of("finding.SPOF.unreachable.message",
                        ("node", node.Name),
                        ("count", unreachable))
                    : LocalizedText.Of("finding.SPOF.shared.message",
                        ("node", node.Name),
                        ("count", dependents))));
        }

        return RuleResult.Applicable(findings);
    }

    /// <summary>
    /// V13 D1.1. Whether the caller genuinely falls when the callee does.
    ///
    /// <strong>Asynchronous is never a hard dependency.</strong> The caller hands the work over and
    /// answers; the queue being down delays the work, it does not fail the request. Counting those
    /// arrows is what made <c>resilient-async</c>'s Queue a single point of failure for the two APIs
    /// that only ever post to it.
    ///
    /// <strong>A declared fallback is what the caller does instead</strong> - unless the callee is
    /// the system of record, where the fallback covers reads and never writes
    /// (<see cref="NodeRoles.IsSystemOfRecord"/>). That single exception is why the same wiring
    /// clears the Cache and keeps the Database flagged, which is the answer both cases deserve.
    ///
    /// <strong>A circuit breaker on its own does not count.</strong> It stops the caller from
    /// hammering something that is already down and it stops the cascade - it does not produce an
    /// answer. Treating it as protection would exempt half the graph for a mechanism that only ever
    /// makes failing cheaper.
    /// </summary>
    private static bool IsHardDependency(ArchitectureGraph graph, Connection edge)
    {
        if (!edge.Synchronous)
            return false;

        if (!graph.ResilienceOf(edge).Fallback)
            return true;

        return graph.NodesById.TryGetValue(edge.TargetNodeId, out var target)
               && NodeRoles.IsSystemOfRecord(target.Type);
    }

    // One finding per node, with the highest severity it earns. MISSING_RESILIENCE only escalates
    // towards a Critical one, so the severity travels inside the fact too (D5.1).
    private RuleFinding Report(
        ArchitectureGraph graph,
        Node node,
        Severity severity,
        decimal affected,
        LocalizedText message)
    {
        graph.Facts.Add(new SinglePointOfFailureFact(node.Id, severity));

        return new RuleFinding(
            Code,
            Area,
            severity,
            message,

            // The two patterns say different things and share what they mean, so the explanation
            // hangs off the code rather than off either variant (V7 D1.1) - but it is no longer the
            // same sentence for every case, because the measurement is not (V13 D4.1).
            Explain(graph, node, affected),
            Suggest(node),
            [FindingElement.Node(node.Id)]);
    }

    /// <summary>
    /// V13 D4.1. What losing this component actually costs, measured rather than asserted.
    ///
    /// <strong>Zero is a result and not a missing number</strong>, and it needs its own sentence.
    /// It is what <c>resilient-async</c>'s database answers: every call into it carries a timeout and
    /// a fallback, so not one request stops being served - and it is still a single point of failure,
    /// because what a fallback cannot give back is the write (D1.1). Printing "0 % of requests" next
    /// to a Warning would read as the analyzer contradicting itself; saying what <em>is</em> lost
    /// instead is the sentence that case deserves.
    ///
    /// <strong>And zero has two readings, not one</strong> (QA B1). On a graph with no entry point -
    /// every component depending on some other, all of it cycle - the walk has nowhere to start, so
    /// the zero means "nothing could be measured", not "every caller is covered". The covered
    /// sentence asserts protections by name, and asserting them over a graph whose every edge is a
    /// bare synchronous call was the analyzer vouching for mechanisms nobody declared.
    ///
    /// V14 B1. Which entry points is <see cref="TrafficRoots"/>'s question and no longer
    /// <c>RootNodes</c>: drawing a telemetry box over that same all-cycle graph gave it a node
    /// nothing points at, and the analyzer went straight from admitting it could not measure to
    /// vouching for protections that were still not there.
    /// </summary>
    private static LocalizedText Explain(ArchitectureGraph graph, Node node, decimal affected)
    {
        if (affected > 0m)
            return LocalizedText.Of("finding.SPOF.explanation.measured",
                ("node", node.Name),
                ("affected", affected));

        return LocalizedText.Of(TrafficRoots.Of(graph).Count == 0
            ? "finding.SPOF.explanation.unmeasured"
            : "finding.SPOF.explanation.covered", ("node", node.Name));
    }

    /// <summary>
    /// V13 D1.2. <strong>What to do about it is not the same sentence for a database and for a
    /// gateway</strong>, and until V13 it was: one constant, "add a LoadBalancer with two equivalent
    /// instances, or replicate the component", handed identically to a Postgres and to somebody
    /// else's payment provider - where the second half is advice you cannot act on at all.
    ///
    /// The pattern is <see cref="UndersizedComponentRule"/>'s, which has picked its suggestion by
    /// type since V4. This makes it the norm rather than that rule's private habit.
    /// </summary>
    private static LocalizedText Suggest(Node node) => LocalizedText.Of(node.Type switch
    {
        NodeType.Database => "finding.SPOF.suggestion.database",
        NodeType.Cache => "finding.SPOF.suggestion.cache",
        NodeType.Queue => "finding.SPOF.suggestion.queue",
        NodeType.Topic => "finding.SPOF.suggestion.topic",
        NodeType.ObjectStorage => "finding.SPOF.suggestion.objectStorage",

        // You cannot give somebody else's service more instances and you cannot fix it (V8 §6.2),
        // so the only move left is deciding what happens when it is not there. The CDN shared this
        // sentence from QA §3.2 until V15 C2, when it stopped being graded at all: the call into it
        // is the outside's, so the fallback this advice names was never one the architect could
        // declare on it.
        NodeType.ExternalService => "finding.SPOF.suggestion.external",

        // V14 B12. Somebody else's too, and with a way out of its own that "add an instance"
        // cannot teach: validate the token locally instead of calling on every request, and
        // decide what a request with a valid session does when the provider is away - which is
        // the fallback that clears this finding.
        NodeType.IdentityProvider => "finding.SPOF.suggestion.identityProvider",

        _ when NodeRoles.IsRouter(node.Type) => "finding.SPOF.suggestion.entryPoint",
        _ => "finding.SPOF.suggestion.instances"
    });

    /// <summary>
    /// A LoadBalancer spreading traffic over ≥2 nodes of the same type is never flagged (D4.x).
    /// It is the redundancy mechanism itself: without this exception the model loops - you add a
    /// balancer to fix a SPOF and the balancer becomes the SPOF, with no way out.
    ///
    /// V15 B1. "Two nodes" is two drawn <em>or</em> one that declares them
    /// (<see cref="BalancedTypes"/>); the loop had quietly reopened along the declared path, and a
    /// balancer has no <c>instances</c> field of its own to close it with.
    /// </summary>
    private static bool IsRedundancyMechanism(ArchitectureGraph graph, Node node) =>
        node.Type == NodeType.LoadBalancer && BalancedTypes(graph, node).Any();

    private static bool HasRedundancy(ArchitectureGraph graph, Node node)
    {
        // QA B2. A system of record's redundancy is declared, never inferred from the wiring.
        // users-db and orders-db serving exactly the same consumers pass every topological test
        // and are still two truths, not two copies - and a balancer spreading over them is
        // routing writes to two primaries. What does clear it is the thing the suggestion asks
        // for, replicas with failover, declared on the component itself - the field the UI
        // already edits.
        if (NodeRoles.IsSystemOfRecord(node.Type))
            return NodeSettings.HasDeclaredReplication(node);

        // QA V14 B4. And a stateless component's redundancy is declared too - not *only*
        // declared, which is B2's rule for the system of record, but declared as one of the ways.
        // The suggestion this rule hands out says "add a second equivalent instance"; the UI edits
        // that field, the node prints "2 inst" on its own card and CapacityModel has read it as
        // "how many copies of me there are" since V4. SPOF was the one place it meant nothing, so
        // the loop advice → field → green closed on a Database and nowhere else.
        //
        // Declaring is enough here for the reason it is not enough there (D1.1): what a fallback
        // cannot give back is the write, and a component with no state has no write only one copy
        // can take. Two of them are interchangeable by construction, which is exactly what two
        // databases are not.
        if (NodeSettings.HasDeclaredInstances(node))
            return true;

        // Explicit: it hangs from a LoadBalancer that spreads over ≥2 nodes of its type. This is
        // the drawing convention for replicas, and it is kept knowing its limit (V14 B11, T14): a
        // balancer over an ApiPublica and an ApiAdmin exempts both, because from the wiring alone
        // two boxes of one type behind one balancer *is* what two copies look like. The way to
        // draw two different things is to name them on one box each with nothing spreading over
        // them - or, since B4, to say "two" on the box that has two.
        return graph.IncomingByTarget[node.Id]
            .Select(c => graph.NodesById.GetValueOrDefault(c.SourceNodeId))
            .Any(source => source is { Type: NodeType.LoadBalancer } && BalancedTypes(graph, source).Contains(node.Type));

        // V14 B11. There used to be a third way out here: an "equivalent sibling" - another node
        // of the same type serving exactly the same consumers - exempted both as replicas. QA B2
        // removed it for systems of record with an argument that applied word for word to
        // everything else: Users and Orders, two services each with two hard dependents, pass
        // every topological test and are still two things, not two copies. What kept it alive was
        // that there was no other way to *say* two stateless boxes were copies; B4 gave the field,
        // so the inference that guessed at it is gone. Redundancy is drawn (a balancer) or
        // declared (instances, replicas). It is never inferred from who happens to call you.
    }

    /// <summary>
    /// Node types this balancer spreads over with at least two instances. Systems of record are
    /// left out (QA B2): copies of a stateless thing are interchangeable, two databases never are,
    /// so a balancer in front of two of them is not the redundancy mechanism - it is a router to
    /// two truths, and it earns no exemption for itself or for what hangs from it.
    ///
    /// <strong>V15 B1. "At least two" is read both ways the product has of saying it</strong>:
    /// two boxes drawn, or one box that declares <see cref="NodeSettings.HasDeclaredInstances"/>.
    /// They are the same fact - V14 B4 made the field mean "how many copies of me there are" to
    /// this rule, and it was still meaning nothing to the balancer in front, so
    /// <c>LB → Api(instances: 2)</c> was a Critical on the balancer while <c>LB → Api + Api2</c> was
    /// clean. The declaration counts only where the field exists: a Gateway with <c>instances</c>
    /// written on it has declared nothing (V3 D3.5), and the balancer over it is still balancing one
    /// box. Not <c>Instances</c>, for the reason <c>HasDeclaredInstances</c> gives - that one
    /// answers 1 for every type that cannot declare, and reads a database's read replicas as
    /// copies.
    /// </summary>
    private static IEnumerable<NodeType> BalancedTypes(ArchitectureGraph graph, Node balancer) =>
        graph.OutgoingBySource[balancer.Id]
            .Select(c => graph.NodesById.GetValueOrDefault(c.TargetNodeId))
            .Where(target => target is not null && !NodeRoles.IsSystemOfRecord(target!.Type))
            .GroupBy(target => target!.Type)
            .Where(byType => byType.Select(target => target!.Id).Distinct().Count() >= RedundantInstancesMin
                             || byType.Any(target => NodeSettings.HasDeclaredInstances(target!)))
            .Select(byType => byType.Key);

    /// <summary>
    /// What traffic can still get to, entering through the roots. Costs O(V · (V+E)) over the
    /// whole rule; irrelevant at the sizes this product handles, and replaceable by a dominator
    /// tree the day it is not (D4.x).
    ///
    /// <strong>V14 B1. The walk follows traffic, and instrumentation is not traffic.</strong> Until
    /// V14 it read every edge and started from every node without dependents, telemetry included -
    /// so <c>Obs → Api</c>, <c>Obs → Svc</c>, <c>Obs → Db</c>, the exact wiring
    /// <c>NO_OBSERVABILITY</c> asks for, made <c>Obs</c> the root of the graph and let it reach
    /// <c>Svc</c> and <c>Db</c> through its own scrape cables. Nothing was left "unreachable"
    /// without the Api, and the flagship finding of the product disappeared the moment the student
    /// followed another rule's advice. The rule had two notions of "this is not traffic" - the
    /// counting side skipped telemetry, the walk did not - and that is the whole lesson of the
    /// V13 bug of the same number. There is one notion now, and it lives here:
    /// <see cref="NodeRoles.IsTelemetryEdge"/> on the way out, <see cref="TrafficRoots.Of"/> on the
    /// way in.
    /// </summary>
    private static HashSet<Guid> ReachableFromRoots(ArchitectureGraph graph, Guid? excluded)
    {
        var reachable = new HashSet<Guid>();
        var pending = new Stack<Guid>(TrafficRoots.Of(graph).Select(n => n.Id).Where(id => id != excluded));

        while (pending.TryPop(out var nodeId))
        {
            if (!reachable.Add(nodeId))
                continue;

            foreach (var edge in graph.OutgoingBySource[nodeId])
            {
                if (edge.TargetNodeId != excluded && !NodeRoles.IsTelemetryEdge(graph, edge))
                    pending.Push(edge.TargetNodeId);
            }
        }

        return reachable;
    }

    /// <summary>
}
