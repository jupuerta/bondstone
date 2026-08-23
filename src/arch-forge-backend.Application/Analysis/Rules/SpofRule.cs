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
        var dependentsByNode = graph.Nodes.ToDictionary(
            node => node.Id,
            node => graph.IncomingByTarget[node.Id]
                .Where(edge => edge.SourceNodeId != node.Id && !NodeRoles.IsRequestFromOutside(graph, edge))
                .Select(c => c.SourceNodeId)
                .Distinct()
                .ToHashSet());

        var hardDependentsByNode = graph.Nodes.ToDictionary(
            node => node.Id,
            node => graph.IncomingByTarget[node.Id]
                .Where(edge => edge.SourceNodeId != node.Id
                               && !NodeRoles.IsRequestFromOutside(graph, edge)
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

            if (IsRedundancyMechanism(graph, node) || HasRedundancy(graph, node, dependentsByNode))
                continue;

            var dependents = hardDependentsByNode[node.Id].Count;

            // Pattern B: how much of the system stops being reachable without it. The walk still
            // reads every edge - passage is about traffic flowing, and a fallback dies with the
            // caller that holds it - but an Observability node stays out of it on both sides
            // (QA B1): it is never a point of passage for requests, and it does not count among
            // what falls, for the same reason CapacityModel gives it an enormous capacity - the
            // telemetry box is nobody's lesson. Counting it meant that wiring the telemetry, the
            // exact thing NO_OBSERVABILITY asks for, could turn a clean entry point into a SPOF.
            var unreachable = node.Type == NodeType.Observability || reachableFromRoots.Count == 0
                ? 0
                : reachableFromRoots.Except(ReachableFromRoots(graph, excluded: node.Id))
                    .Count(id => id != node.Id
                                 && graph.NodesById.TryGetValue(id, out var lost)
                                 && lost.Type != NodeType.Observability
                                 && !NodeRoles.IsTrafficOrigin(lost.Type));

            var passage = unreachable >= UnreachableMin;
            var shared = dependents >= SharedDependencyMin;

            if (!passage && !shared)
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
            var sharedSeverity = dependents >= SharedDependencyCritical ? Severity.Critical : Severity.Warning;

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
    /// </summary>
    private static LocalizedText Explain(ArchitectureGraph graph, Node node, decimal affected)
    {
        if (affected > 0m)
            return LocalizedText.Of("finding.SPOF.explanation.measured",
                ("node", node.Name),
                ("affected", affected));

        return LocalizedText.Of(graph.RootNodes.Count == 0
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
        NodeType.ObjectStorage => "finding.SPOF.suggestion.objectStorage",

        // You cannot give somebody else's service more instances and you cannot fix it (V8 §6.2),
        // so the only move left is deciding what happens when it is not there. A CDN is the same
        // situation (QA §3.2): the honest advice is a fallback to the origin or a second provider.
        NodeType.ExternalService or NodeType.Cdn => "finding.SPOF.suggestion.external",

        _ when NodeRoles.IsRouter(node.Type) => "finding.SPOF.suggestion.entryPoint",
        _ => "finding.SPOF.suggestion.instances"
    });

    /// <summary>
    /// A LoadBalancer spreading traffic over ≥2 nodes of the same type is never flagged (D4.x).
    /// It is the redundancy mechanism itself: without this exception the model loops - you add a
    /// balancer to fix a SPOF and the balancer becomes the SPOF, with no way out.
    /// </summary>
    private static bool IsRedundancyMechanism(ArchitectureGraph graph, Node node) =>
        node.Type == NodeType.LoadBalancer && BalancedTypes(graph, node).Any();

    private static bool HasRedundancy(ArchitectureGraph graph, Node node, Dictionary<Guid, HashSet<Guid>> dependentsByNode)
    {
        // QA B2. A system of record's redundancy is declared, never inferred from the wiring.
        // users-db and orders-db serving exactly the same consumers pass every topological test
        // and are still two truths, not two copies - and a balancer spreading over them is
        // routing writes to two primaries. What does clear it is the thing the suggestion asks
        // for, replicas with failover, declared on the component itself - the field the UI
        // already edits.
        if (NodeRoles.IsSystemOfRecord(node.Type))
            return NodeSettings.HasDeclaredReplication(node);

        // Explicit: it hangs from a LoadBalancer that spreads over ≥2 nodes of its type.
        var behindBalancer = graph.IncomingByTarget[node.Id]
            .Select(c => graph.NodesById.GetValueOrDefault(c.SourceNodeId))
            .Any(source => source is { Type: NodeType.LoadBalancer } && BalancedTypes(graph, source).Contains(node.Type));

        if (behindBalancer)
            return true;

        var dependents = dependentsByNode[node.Id];

        // Equivalent sibling: same type, and serving exactly the same consumers. V1 asked only for
        // "some dependent in common", which breaks with microservices - Users, Orders and Payments
        // are all Services hanging off the same gateway and would exempt each other as if they
        // were replicas. Serving nobody is not redundancy either, so an empty set does not count.
        //
        // Compared over every dependent and not only the hard ones (D1.1): whether two replicas are
        // interchangeable is a question about who they serve, and a consumer that holds a fallback
        // is still one of the two things they both serve.
        return dependents.Count > 0 && graph.Nodes.Any(other =>
            other.Id != node.Id &&
            other.Type == node.Type &&
            dependentsByNode[other.Id].SetEquals(dependents));
    }

    /// <summary>
    /// Node types this balancer spreads over with at least two instances. Systems of record are
    /// left out (QA B2): copies of a stateless thing are interchangeable, two databases never are,
    /// so a balancer in front of two of them is not the redundancy mechanism - it is a router to
    /// two truths, and it earns no exemption for itself or for what hangs from it.
    /// </summary>
    private static IEnumerable<NodeType> BalancedTypes(ArchitectureGraph graph, Node balancer) =>
        graph.OutgoingBySource[balancer.Id]
            .Select(c => graph.NodesById.GetValueOrDefault(c.TargetNodeId))
            .Where(target => target is not null && !NodeRoles.IsSystemOfRecord(target!.Type))
            .GroupBy(target => target!.Type)
            .Where(byType => byType.Select(target => target!.Id).Distinct().Count() >= RedundantInstancesMin)
            .Select(byType => byType.Key);

    /// <summary>
    /// What traffic can still get to, entering through the roots. Costs O(V · (V+E)) over the
    /// whole rule; irrelevant at the sizes this product handles, and replaceable by a dominator
    /// tree the day it is not (D4.x).
    /// </summary>
    private static HashSet<Guid> ReachableFromRoots(ArchitectureGraph graph, Guid? excluded)
    {
        var reachable = new HashSet<Guid>();
        var pending = new Stack<Guid>(graph.RootNodes.Select(n => n.Id).Where(id => id != excluded));

        while (pending.TryPop(out var nodeId))
        {
            if (!reachable.Add(nodeId))
                continue;

            foreach (var edge in graph.OutgoingBySource[nodeId])
            {
                if (edge.TargetNodeId != excluded)
                    pending.Push(edge.TargetNodeId);
            }
        }

        return reachable;
    }
}
