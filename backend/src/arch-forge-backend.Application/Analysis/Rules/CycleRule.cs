using bondstone_backend.Domain.Entities;

using bondstone_backend.Application.Localization;

namespace bondstone_backend.Application.Analysis.Rules;

public class CycleRule : IArchitectureRule
{
    public string Code => RuleCodes.Cycle;
    public Area Area => Area.Coupling;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Producer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.CYCLE.passed.message");

    public LocalizedText PassedExplanation => LocalizedText.Of("finding.CYCLE.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        if (!graph.Connections.Any(edge => !NodeRoles.IsTelemetryEdge(graph, edge)))
            return RuleResult.NotApplicable("rule.na.CYCLE.noCalls");

        var findings = new List<RuleFinding>();

        foreach (var cycle in FindCycles(graph))
        {
            var severity = cycle.All(edge => edge.Synchronous) ? Severity.Critical : Severity.Warning;

            foreach (var edge in cycle)
                graph.Facts.Add(new CyclicConnectionFact(edge.Id));

            var names = cycle.Select(edge => graph.NodesById[edge.SourceNodeId].Name).ToList();

            findings.Add(new RuleFinding(
                Code,
                Area,
                severity,
                LocalizedText.Of("finding.CYCLE.detected.message",
                    ("count", cycle.Count),
                    ("walk", string.Join(" → ", names) + $" → {names[0]}")),
                LocalizedText.Of("finding.CYCLE.detected.explanation"),
                Suggest(graph, cycle),
                Interleave(cycle)));
        }

        return RuleResult.Applicable(findings);
    }

    private static LocalizedText Suggest(ArchitectureGraph graph, IReadOnlyList<Connection> cycle)
    {
        var participants = cycle
            .Select(edge => graph.NodesById.GetValueOrDefault(edge.SourceNodeId))
            .Where(node => node is not null)
            .ToList();

        if (participants.Any(node => NodeRoles.IsDataStore(node!.Type)))
            return LocalizedText.Of("finding.CYCLE.detected.suggestion.sharedData");

        return LocalizedText.Of(cycle.All(edge => edge.Synchronous)
            ? "finding.CYCLE.detected.suggestion.synchronous"
            : "finding.CYCLE.detected.suggestion.decouple");
    }

    private static List<FindingElement> Interleave(IReadOnlyList<Connection> cycle)
    {
        var elements = new List<FindingElement> { FindingElement.Node(cycle[0].SourceNodeId) };

        foreach (var edge in cycle)
        {
            elements.Add(FindingElement.Connection(edge.Id));
            elements.Add(FindingElement.Node(edge.TargetNodeId));
        }

        return elements;
    }

    private static List<List<Connection>> FindCycles(ArchitectureGraph graph)
    {
        var cycles = new List<List<Connection>>();
        var reported = new HashSet<string>();
        var visited = new HashSet<Guid>();
        var path = new List<Connection>();
        var onPath = new HashSet<Guid>();

        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
            Walk(node.Id);

        return cycles;

        void Walk(Guid nodeId)
        {
            if (!visited.Add(nodeId))
                return;

            onPath.Add(nodeId);

            foreach (var edge in graph.OutgoingBySource[nodeId].OrderBy(c => c.TargetNodeId).ThenBy(c => c.Id))
            {
                if (NodeRoles.IsTelemetryEdge(graph, edge))
                    continue;

                if (onPath.Contains(edge.TargetNodeId))
                {
                    var cycle = path.SkipWhile(e => e.SourceNodeId != edge.TargetNodeId).Append(edge).ToList();
                    if (cycle[0].SourceNodeId == edge.TargetNodeId)
                        Record(cycle);

                    continue;
                }

                path.Add(edge);
                Walk(edge.TargetNodeId);
                path.RemoveAt(path.Count - 1);
            }

            onPath.Remove(nodeId);
        }

        void Record(List<Connection> cycle)
        {
            var start = cycle.Select((edge, index) => (edge.SourceNodeId, index))
                .OrderBy(entry => entry.SourceNodeId)
                .First().index;

            var canonical = cycle.Skip(start).Concat(cycle.Take(start)).ToList();

            if (reported.Add(string.Join("|", canonical.Select(e => e.Id))))
                cycles.Add(canonical);
        }
    }
}
