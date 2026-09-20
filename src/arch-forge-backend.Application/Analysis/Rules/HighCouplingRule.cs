using bondstone_backend.Domain.Entities;
using bondstone_backend.Domain.Enums;

using bondstone_backend.Application.Localization;

namespace bondstone_backend.Application.Analysis.Rules;

public class HighCouplingRule : IArchitectureRule
{
    public string Code => RuleCodes.HighCoupling;
    public Area Area => Area.Coupling;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.HIGH_COUPLING.passed.message");

    public LocalizedText PassedExplanation => LocalizedText.Of("finding.HIGH_COUPLING.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var coupled = graph.Nodes
            .Where(node => !NodeRoles.IsTrafficOrigin(node.Type) && !NodeRoles.IsTelemetry(node.Type)
                           && node.Type != NodeType.Topic)
            .Select(node => (Node: node, Edges: graph.OutgoingBySource[node.Id]
                .Where(edge => !NodeRoles.IsTelemetryEdge(graph, edge))
                .ToList()))
            .Where(entry => entry.Edges.Count > 0)
            .ToList();

        if (coupled.Count == 0)
            return RuleResult.NotApplicable("rule.na.HIGH_COUPLING.noCalls");

        var findings = new List<RuleFinding>();

        foreach (var (node, edges) in coupled)
        {
            var dependencies = FanOutOf(graph, node, edges);

            var router = NodeRoles.IsRouter(node.Type);
            var warning = router ? RuleThresholds.RouterCouplingWarning : RuleThresholds.CouplingWarning;
            var critical = router ? RuleThresholds.RouterCouplingCritical : RuleThresholds.CouplingCritical;

            if (dependencies < warning)
                continue;

            var severity = dependencies >= critical ? Severity.Critical : Severity.Warning;

            List<FindingElement> elements = [FindingElement.Node(node.Id)];
            elements.AddRange(edges.Select(e => FindingElement.Connection(e.Id)));

            findings.Add(new RuleFinding(
                Code,
                Area,
                severity,
                LocalizedText.Of("finding.HIGH_COUPLING.fanOut.message",
                    ("node", node.Name),
                    ("count", dependencies)),
                LocalizedText.Of("finding.HIGH_COUPLING.fanOut.explanation"),
                Suggest(node),
                elements));
        }

        return RuleResult.Applicable(findings);
    }

    private static int FanOutOf(ArchitectureGraph graph, Node node, IReadOnlyList<Connection> edges)
    {
        var targets = edges.Select(e => e.TargetNodeId).Distinct().ToList();

        if (node.Type != NodeType.LoadBalancer)
            return targets.Count;

        return targets
            .Select(id => graph.NodesById.GetValueOrDefault(id))
            .Where(target => target is not null)
            .Select(target => target!.Type)
            .Distinct()
            .Count();
    }

    private static LocalizedText Suggest(Node node) => LocalizedText.Of(node.Type switch
    {
        _ when NodeRoles.IsRouter(node.Type) => "finding.HIGH_COUPLING.fanOut.suggestion.route",

        NodeType.Api or NodeType.Service or NodeType.Worker => "finding.HIGH_COUPLING.fanOut.suggestion.split",

        _ => "finding.HIGH_COUPLING.fanOut.suggestion.review"
    });
}
