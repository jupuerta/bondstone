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

        var reachableFromRoots = ReachableFromRoots(graph, excluded: null);

        var load = LoadModel.Of(graph);
        var findings = new List<RuleFinding>();

        foreach (var node in graph.Nodes)
        {
            if (NodeRoles.IsTrafficOrigin(node.Type))
                continue;

            if (NodeRoles.IsProviderEdge(node.Type))
                continue;

            if (IsRedundancyMechanism(graph, node) || HasRedundancy(graph, node))
                continue;

            var dependents = hardDependentsByNode[node.Id].Count;

            var unreachable = reachableFromRoots.Count == 0
                ? 0
                : reachableFromRoots.Except(ReachableFromRoots(graph, excluded: node.Id))
                    .Count(id => id != node.Id
                                 && graph.NodesById.TryGetValue(id, out var lost)
                                 && !NodeRoles.IsTrafficOrigin(lost.Type));

            var passage = unreachable >= UnreachableMin;
            var shared = dependents >= SharedDependencyMin;

            var identity = node.Type == NodeType.IdentityProvider && dependents >= 1;

            if (!passage && !shared && !identity)
                continue;

            var affected = OutageImpact.OfLosing(graph, load, node.Id);
            var passageSeverity = affected > 0m ? Severity.Critical : Severity.Warning;

            var sharedSeverity = identity
                ? passageSeverity
                : dependents >= SharedDependencyCritical ? Severity.Critical : Severity.Warning;

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

    private static bool IsHardDependency(ArchitectureGraph graph, Connection edge)
    {
        if (!edge.Synchronous)
            return false;

        if (!graph.ResilienceOf(edge).Fallback)
            return true;

        return graph.NodesById.TryGetValue(edge.TargetNodeId, out var target)
               && NodeRoles.IsSystemOfRecord(target.Type);
    }

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

            Explain(graph, node, affected),
            Suggest(node),
            [FindingElement.Node(node.Id)]);
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

}
