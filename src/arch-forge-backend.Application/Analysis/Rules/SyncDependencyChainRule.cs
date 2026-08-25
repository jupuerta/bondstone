using arch_forge_backend.Domain.Entities;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

public class SyncDependencyChainRule : IArchitectureRule
{
    public string Code => RuleCodes.SyncChain;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.SYNC_CHAIN.passed.message");

    public LocalizedText PassedExplanation => LocalizedText.Of("finding.SYNC_CHAIN.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var synchronous = graph.Connections
            .Where(c => c.Synchronous
                        && !NodeRoles.IsNotYourCall(graph, c)
                        && !NodeRoles.IsTelemetryEdge(graph, c))
            .ToList();

        if (synchronous.Count == 0)
            return RuleResult.NotApplicable("rule.na.SYNC_CHAIN.noSyncCall");

        var cyclic = graph.Facts.OfType<CyclicConnectionFact>().Select(f => f.ConnectionId).ToHashSet();
        var edges = synchronous.Where(c => !cyclic.Contains(c.Id)).ToList();

        var bySource = edges.ToLookup(c => c.SourceNodeId);
        var withIncoming = edges.Select(c => c.TargetNodeId).ToHashSet();

        var longest = new Dictionary<Guid, (List<Connection> Path, int Logic)>();
        var findings = new List<RuleFinding>();
        var reported = new HashSet<Guid>();

        foreach (var node in graph.Nodes.Where(n => !withIncoming.Contains(n.Id)))
        {
            var (path, logicAhead) = LongestFrom(node.Id);

            var components = (IsLogic(node.Id) ? 1 : 0) + logicAhead;
            if (components < RuleThresholds.SyncChainWarning)
                continue;

            var walk = path.Select(edge => edge.TargetNodeId).Prepend(node.Id).ToList();
            var from = walk.FindIndex(IsLogic);
            var head = walk[from];

            if (!reported.Add(head))
                continue;

            path = path.Skip(from).ToList();
            var start = graph.NodesById[head];

            var severity = components >= RuleThresholds.SyncChainCritical ? Severity.Critical : Severity.Warning;

            findings.Add(new RuleFinding(
                Code,
                Area,
                severity,
                LocalizedText.Of("finding.SYNC_CHAIN.long.message",
                    ("node", start.Name),
                    ("count", components)),
                LocalizedText.Of("finding.SYNC_CHAIN.long.explanation"),
                LocalizedText.Of(EndsInARead(path)
                    ? "finding.SYNC_CHAIN.long.suggestion.read"
                    : "finding.SYNC_CHAIN.long.suggestion"),
                Interleave(start.Id, path)));
        }

        return RuleResult.Applicable(findings);

        bool IsLogic(Guid nodeId) =>
            graph.NodesById.TryGetValue(nodeId, out var node) && NodeRoles.IsLogicComponent(node.Type);

        bool EndsInARead(IReadOnlyList<Connection> chain) =>
            chain.Count > 0
            && graph.NodesById.TryGetValue(chain[^1].TargetNodeId, out var tail)
            && NodeRoles.IsDataStore(tail.Type)
            && graph.ResilienceOf(chain[^1]).Reads;

        (List<Connection> Path, int Logic) LongestFrom(Guid nodeId, HashSet<Guid>? onPath = null)
        {
            if (longest.TryGetValue(nodeId, out var cached))
                return cached;

            onPath ??= [];
            if (!onPath.Add(nodeId))
                return ([], 0);

            var best = (Path: new List<Connection>(), Logic: 0);
            var found = false;

            foreach (var edge in bySource[nodeId].OrderBy(c => c.TargetNodeId).ThenBy(c => c.Id))
            {
                var candidate = LongestFrom(edge.TargetNodeId, onPath);
                var logic = candidate.Logic + (IsLogic(edge.TargetNodeId) ? 1 : 0);

                if (found && logic <= best.Logic)
                    continue;

                best = (candidate.Path.Prepend(edge).ToList(), logic);
                found = true;
            }

            onPath.Remove(nodeId);
            longest[nodeId] = best;
            return best;
        }
    }

    private static List<FindingElement> Interleave(Guid startNodeId, IReadOnlyList<Connection> path)
    {
        var elements = new List<FindingElement> { FindingElement.Node(startNodeId) };

        foreach (var edge in path)
        {
            elements.Add(FindingElement.Connection(edge.Id));
            elements.Add(FindingElement.Node(edge.TargetNodeId));
        }

        return elements;
    }
}
