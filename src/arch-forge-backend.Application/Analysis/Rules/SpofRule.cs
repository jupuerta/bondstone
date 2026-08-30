using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Analysis.Rules;

public class SpofRule : IArchitectureRule
{
    private const int SharedDependencyMin = 2;
    private const int SharedDependencyCritical = 3;
    private const int UnreachableMin = 2;
    private const int RedundantInstancesMin = 2;

    private static readonly HashSet<Guid> Nothing = [];

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

        if (!dependentsByNode.Values.Any(dependents => dependents.Count > 0))
            return RuleResult.NotApplicable("rule.na.SPOF.noDependency");

        var reachableFromRoots = ReachableFromRoots(graph, Nothing);

        var load = LoadModel.Of(graph);
        var findings = new List<RuleFinding>();
        var reportedSets = new HashSet<Guid>();

        foreach (var node in graph.Nodes)
        {
            if (NodeRoles.IsTrafficOrigin(node.Type))
                continue;

            if (NodeRoles.IsProviderEdge(node.Type))
                continue;

            if (IsRedundancyMechanism(graph, node))
                continue;

            if (UnitOf(graph, node) is not { } unit)
                continue;

            if (unit.Zone is not null && !reportedSets.Add(unit.Anchor))
                continue;

            var ids = unit.Nodes.Select(copy => copy.Id).ToHashSet();

            var dependents = unit.Nodes
                .SelectMany(copy => hardDependentsByNode[copy.Id])
                .Where(id => !ids.Contains(id))
                .Distinct()
                .Count();

            var unreachable = reachableFromRoots.Count == 0
                ? 0
                : reachableFromRoots.Except(ReachableFromRoots(graph, ids))
                    .Count(id => !ids.Contains(id)
                                 && graph.NodesById.TryGetValue(id, out var lost)
                                 && !NodeRoles.IsTrafficOrigin(lost.Type));

            var passage = unreachable >= UnreachableMin;
            var shared = dependents >= SharedDependencyMin;

            var identity = node.Type == NodeType.IdentityProvider && dependents >= 1;

            if (!passage && !shared && !identity)
                continue;

            if (unit.Zone is { } zone)
            {
                findings.Add(Report(graph, unit.Nodes, Severity.Warning,
                    LocalizedText.Of("finding.SPOF.sameZone.message",
                        ("names", Names(unit.Nodes)),
                        ("count", unit.Nodes.Count),
                        ("zone", zone)),
                    LocalizedText.Of("finding.SPOF.sameZone.explanation", ("zone", zone)),
                    LocalizedText.Of("finding.SPOF.suggestion.sameZone")));

                continue;
            }

            var affected = OutageImpact.OfLosing(graph, load, node.Id);
            var passageSeverity = affected > 0m ? Severity.Critical : Severity.Warning;

            var sharedSeverity = identity
                ? passageSeverity
                : dependents >= SharedDependencyCritical ? Severity.Critical : Severity.Warning;

            var byPassage = passage && (!shared || passageSeverity >= sharedSeverity);

            findings.Add(Report(graph, unit.Nodes, byPassage ? passageSeverity : sharedSeverity,
                byPassage
                    ? LocalizedText.Of("finding.SPOF.unreachable.message",
                        ("node", node.Name),
                        ("count", unreachable))
                    : LocalizedText.Of("finding.SPOF.shared.message",
                        ("node", node.Name),
                        ("count", dependents)),
                Explain(graph, node, affected),
                Suggest(node)));
        }

        return RuleResult.Applicable(findings);
    }

    // A fallback discounts the dependency only when there is something to answer with: not towards
    // a store, not for a declared write, and not from a router, which has nothing to answer with
    // (QA 30-08, §3.5 and 7.4).
    private static bool IsHardDependency(ArchitectureGraph graph, Connection edge)
    {
        if (!edge.Synchronous)
            return false;

        var resilience = graph.ResilienceOf(edge);

        if (!resilience.Fallback || resilience.DeclaresWrite)
            return true;

        if (NodeRoles.ForwardsRequest(graph, edge))
            return true;

        return graph.NodesById.TryGetValue(edge.TargetNodeId, out var target)
               && NodeRoles.IsSystemOfRecord(target.Type);
    }

    private RuleFinding Report(
        ArchitectureGraph graph,
        IReadOnlyList<Node> unit,
        Severity severity,
        LocalizedText message,
        LocalizedText explanation,
        LocalizedText suggestion)
    {
        foreach (var node in unit)
            graph.Facts.Add(new SinglePointOfFailureFact(node.Id, severity));

        return new RuleFinding(
            Code,
            Area,
            severity,
            message,

            explanation,
            suggestion,
            unit.Select(node => FindingElement.Node(node.Id)).ToList());
    }

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

    private static LocalizedText Suggest(Node node) => LocalizedText.Of(node.Type switch
    {
        NodeType.Database => "finding.SPOF.suggestion.database",
        NodeType.Cache => "finding.SPOF.suggestion.cache",
        NodeType.Queue => "finding.SPOF.suggestion.queue",
        NodeType.Topic => "finding.SPOF.suggestion.topic",
        NodeType.ObjectStorage => "finding.SPOF.suggestion.objectStorage",

        NodeType.ExternalService => "finding.SPOF.suggestion.external",

        NodeType.IdentityProvider => "finding.SPOF.suggestion.identityProvider",

        _ when NodeRoles.IsRouter(node.Type) => "finding.SPOF.suggestion.entryPoint",
        _ => "finding.SPOF.suggestion.instances"
    });

    private static string Names(IEnumerable<Node> unit) =>
        string.Join(", ", unit.Select(node => $"'{node.Name}'"));

    // What gets graded as one box. Null means the node is redundant and stays exempt, which is the
    // answer for everything that does not declare a zone: the opt-in of V20 Z3 lives here and nowhere
    // else. A zone turns the exemption into a warning instead of removing it.
    private static Unit? UnitOf(ArchitectureGraph graph, Node node)
    {
        if (!HasRedundancy(graph, node))
            return new Unit(node.Id, [node], null);

        if (PlaceableCopies(graph, node) is not { } copies)
            return null;

        var declared = copies.Select(NodeSettings.Zone).OfType<string>().Distinct().ToList();

        // Nobody says where the copies are: the opt-in stays. Two zones: spread. One zone declared —
        // by every copy or by one of them — is one zone: a copy that does not say where it is has not
        // been shown to be anywhere else (QA 30-08, §1.5), and «I do not know» is not «elsewhere».
        return declared switch
        {
            [] => null,
            [{ } zone] => new Unit(copies[0].Id, copies, zone),
            _ => null
        };
    }

    // The copies a drawing can place. Siblings behind a balancer have one zone each; instances and
    // replicas are a counter inside a single box, so the box's zone is theirs. A store cleared by
    // replicas alone has no placeable copies at all: the model offers no way to put a stored replica
    // somewhere else, and a finding whose only exit is deleting the zone would teach hiding it.
    private static List<Node>? PlaceableCopies(ArchitectureGraph graph, Node node)
    {
        var (anchor, type) = NodeRoles.ReplicaSetOf(graph, node);

        if (anchor != node.Id)
            return graph.OutgoingBySource[anchor]
                .Select(edge => graph.NodesById.GetValueOrDefault(edge.TargetNodeId))
                .Where(target => target?.Type == type)
                .Select(target => target!)
                .DistinctBy(target => target.Id)
                .OrderBy(target => target.Id)
                .ToList();

        return NodeSettings.HasDeclaredInstances(node) ? [node] : null;
    }

    private static bool IsRedundancyMechanism(ArchitectureGraph graph, Node node) =>
        node.Type == NodeType.LoadBalancer && BalancedTypes(graph, node).Any();

    private static bool HasRedundancy(ArchitectureGraph graph, Node node)
    {
        if (NodeRoles.IsSystemOfRecord(node.Type))
            return NodeSettings.HasDeclaredReplication(node);

        if (NodeSettings.HasDeclaredInstances(node))
            return true;

        return graph.IncomingByTarget[node.Id]
            .Select(c => graph.NodesById.GetValueOrDefault(c.SourceNodeId))
            .Any(source => source is { Type: NodeType.LoadBalancer } && BalancedTypes(graph, source).Contains(node.Type));

    }

    private static IEnumerable<NodeType> BalancedTypes(ArchitectureGraph graph, Node balancer) =>
        graph.OutgoingBySource[balancer.Id]
            .Select(c => graph.NodesById.GetValueOrDefault(c.TargetNodeId))
            .Where(target => target is not null && !NodeRoles.IsSystemOfRecord(target!.Type))
            .GroupBy(target => target!.Type)
            .Where(byType => byType.Select(target => target!.Id).Distinct().Count() >= RedundantInstancesMin
                             || byType.Any(target => NodeSettings.HasDeclaredInstances(target!)))
            .Select(byType => byType.Key);

    private static HashSet<Guid> ReachableFromRoots(ArchitectureGraph graph, IReadOnlySet<Guid> excluded)
    {
        var reachable = new HashSet<Guid>();
        var pending = new Stack<Guid>(TrafficRoots.Of(graph).Select(n => n.Id).Where(id => !excluded.Contains(id)));

        while (pending.TryPop(out var nodeId))
        {
            if (!reachable.Add(nodeId))
                continue;

            foreach (var edge in graph.OutgoingBySource[nodeId])
            {
                if (!excluded.Contains(edge.TargetNodeId) && !NodeRoles.IsTelemetryEdge(graph, edge))
                    pending.Push(edge.TargetNodeId);
            }
        }

        return reachable;
    }

    private readonly record struct Unit(Guid Anchor, IReadOnlyList<Node> Nodes, string? Zone);
}
