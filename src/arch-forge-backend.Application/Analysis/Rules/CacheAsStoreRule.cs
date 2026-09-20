using bondstone_backend.Application.Localization;
using bondstone_backend.Domain.Entities;
using bondstone_backend.Domain.Enums;

namespace bondstone_backend.Application.Analysis.Rules;

public class CacheAsStoreRule : IArchitectureRule
{
    public string Code => RuleCodes.CacheAsStore;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.CACHE_AS_STORE.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.CACHE_AS_STORE.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var caches = graph.Nodes
            .Where(n => n.Type == NodeType.Cache)
            .Where(n => graph.IncomingByTarget[n.Id].Any(edge => !NodeRoles.IsRequestFromOutside(graph, edge)))
            .ToList();

        if (caches.Count == 0)
            return RuleResult.NotApplicable("rule.na.CACHE_AS_STORE.noCache");

        var findings = caches
            .Where(cache => !HasAnOrigin(graph, cache))
            .OrderBy(cache => cache.Id)
            .Select(cache =>
            {
                List<FindingElement> elements = [FindingElement.Node(cache.Id)];
                elements.AddRange(graph.IncomingByTarget[cache.Id]
                    .Where(edge => !NodeRoles.IsRequestFromOutside(graph, edge))
                    .Select(edge => FindingElement.Connection(edge.Id)));

                return new RuleFinding(
                    Code,
                    Area,
                    DefaultSeverity,
                    LocalizedText.Of("finding.CACHE_AS_STORE.noOrigin.message", ("node", cache.Name)),
                    LocalizedText.Of("finding.CACHE_AS_STORE.noOrigin.explanation", ("node", cache.Name)),
                    LocalizedText.Of("finding.CACHE_AS_STORE.noOrigin.suggestion"),
                    elements);
            })
            .ToList();

        return RuleResult.Applicable(findings);
    }

    private static bool HasAnOrigin(ArchitectureGraph graph, Node cache)
    {
        var visited = new HashSet<Guid>();

        return graph.IncomingByTarget[cache.Id]
            .Where(edge => !NodeRoles.IsRequestFromOutside(graph, edge))
            .Select(edge => edge.SourceNodeId)
            .Any(userId => ReachesSystemOfRecord(graph, userId, visited));
    }

    private static bool ReachesSystemOfRecord(ArchitectureGraph graph, Guid fromId, HashSet<Guid> visited)
    {
        if (!visited.Add(fromId))
            return false;

        if (!graph.NodesById.TryGetValue(fromId, out var node))
            return false;

        if (NodeRoles.IsSystemOfRecord(node.Type))
            return true;

        return graph.OutgoingBySource[fromId]
            .OrderBy(edge => edge.TargetNodeId)
            .Any(edge => ReachesSystemOfRecord(graph, edge.TargetNodeId, visited));
    }
}
