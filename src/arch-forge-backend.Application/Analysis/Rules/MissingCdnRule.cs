using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Analysis.Rules;

public class MissingCdnRule : IArchitectureRule
{
    public string Code => RuleCodes.MissingCdn;
    public Area Area => Area.Scalability;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.MISSING_CDN.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.MISSING_CDN.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var load = LoadModel.Of(graph);
        if (load.IsIdle)
            return RuleResult.NotApplicable("rule.na.MISSING_CDN.noLoad");

        var busy = EntryPoints.PublicFacing(graph)
            .Where(door => !graph.OrphanNodes.Contains(door.Id))
            .Where(door => load.LoadOf(door.Id) >= RuleThresholds.CdnWorthTraffic)
            .ToList();

        if (busy.Count == 0)
            return RuleResult.NotApplicable(LocalizedText.Of(
                "rule.na.MISSING_CDN.lowTraffic", ("traffic", RuleThresholds.CdnWorthTraffic)));

        var findings = busy
            .Where(door => !HasCdnUpstream(graph, door))
            .Select(door => new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                LocalizedText.Of("finding.MISSING_CDN.direct.message",
                    ("node", door.Name),
                    ("loadRps", load.LoadOf(door.Id))),
                LocalizedText.Of("finding.MISSING_CDN.direct.explanation", ("node", door.Name)),
                LocalizedText.Of("finding.MISSING_CDN.direct.suggestion"),
                [FindingElement.Node(door.Id)]))
            .ToList();

        return RuleResult.Applicable(findings);
    }

    private static bool HasCdnUpstream(ArchitectureGraph graph, Node door)
    {
        var seen = new HashSet<Guid> { door.Id };
        var pending = new Queue<Guid>([door.Id]);

        while (pending.TryDequeue(out var nodeId))
        {
            foreach (var edge in graph.IncomingByTarget[nodeId])
            {
                if (NodeRoles.IsTelemetryEdge(graph, edge) || !seen.Add(edge.SourceNodeId))
                    continue;

                if (graph.NodesById.TryGetValue(edge.SourceNodeId, out var source) && source.Type == NodeType.Cdn)
                    return true;

                pending.Enqueue(edge.SourceNodeId);
            }
        }

        return false;
    }
}
