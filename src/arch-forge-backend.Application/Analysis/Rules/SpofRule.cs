using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V2 D4.x. Two patterns, because with the invariant "source depends on target" (D3.3) a gateway
/// has zero dependents: defining a SPOF only as "≥2 components depend on it" would never have
/// fired on the product's flagship example.
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
        var dependentsByNode = graph.Nodes.ToDictionary(
            node => node.Id,
            node => graph.IncomingByTarget[node.Id].Select(c => c.SourceNodeId).Distinct().ToHashSet());

        // Nothing to check while nobody depends on anybody.
        if (!dependentsByNode.Values.Any(dependents => dependents.Count > 0))
            return RuleResult.NotApplicable;

        var reachableFromRoots = ReachableFromRoots(graph, excluded: null);
        var findings = new List<RuleFinding>();

        foreach (var node in graph.Nodes)
        {
            if (IsRedundancyMechanism(graph, node) || HasRedundancy(graph, node, dependentsByNode))
                continue;

            var dependents = dependentsByNode[node.Id].Count;

            // Pattern B: how much of the system stops being reachable without it. Always Critical
            // - a single point of passage takes everything behind it down with it.
            var unreachable = reachableFromRoots.Count == 0
                ? 0
                : reachableFromRoots.Except(ReachableFromRoots(graph, excluded: node.Id)).Count(id => id != node.Id);

            if (unreachable >= UnreachableMin)
            {
                findings.Add(Report(graph, node, Severity.Critical,
                    LocalizedText.Of("finding.SPOF.unreachable.message",
                        ("node", node.Name),
                        ("count", unreachable))));
                continue;
            }

            // Pattern A: shared dependency, graded by how many depend on it.
            if (dependents < SharedDependencyMin)
                continue;

            var severity = dependents >= SharedDependencyCritical ? Severity.Critical : Severity.Warning;

            findings.Add(Report(graph, node, severity,
                LocalizedText.Of("finding.SPOF.shared.message",
                    ("node", node.Name),
                    ("count", dependents))));
        }

        return RuleResult.Applicable(findings);
    }

    // One finding per node, with the highest severity it earns. MISSING_RESILIENCE only escalates
    // towards a Critical one, so the severity travels inside the fact too (D5.1).
    private RuleFinding Report(ArchitectureGraph graph, Node node, Severity severity, LocalizedText message)
    {
        graph.Facts.Add(new SinglePointOfFailureFact(node.Id, severity));

        return new RuleFinding(
            Code,
            Area,
            severity,
            message,
            // The two patterns say different things and share what they mean and what to do about
            // it, so the explanation and the suggestion hang off the code rather than off either
            // variant (V7 D1.1).
            LocalizedText.Of("finding.SPOF.explanation"),
            LocalizedText.Of("finding.SPOF.suggestion"),
            [FindingElement.Node(node.Id)]);
    }

    /// <summary>
    /// A LoadBalancer spreading traffic over ≥2 nodes of the same type is never flagged (D4.x).
    /// It is the redundancy mechanism itself: without this exception the model loops - you add a
    /// balancer to fix a SPOF and the balancer becomes the SPOF, with no way out.
    /// </summary>
    private static bool IsRedundancyMechanism(ArchitectureGraph graph, Node node) =>
        node.Type == NodeType.LoadBalancer && BalancedTypes(graph, node).Any();

    private static bool HasRedundancy(ArchitectureGraph graph, Node node, Dictionary<Guid, HashSet<Guid>> dependentsByNode)
    {
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
        return dependents.Count > 0 && graph.Nodes.Any(other =>
            other.Id != node.Id &&
            other.Type == node.Type &&
            dependentsByNode[other.Id].SetEquals(dependents));
    }

    /// <summary>Node types this balancer spreads over with at least two instances.</summary>
    private static IEnumerable<NodeType> BalancedTypes(ArchitectureGraph graph, Node balancer) =>
        graph.OutgoingBySource[balancer.Id]
            .Select(c => graph.NodesById.GetValueOrDefault(c.TargetNodeId))
            .Where(target => target is not null)
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
