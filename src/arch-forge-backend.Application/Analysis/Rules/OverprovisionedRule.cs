using arch_forge_backend.Application.Simulation;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

public class OverprovisionedRule : IArchitectureRule
{
    public string Code => RuleCodes.Overprovisioned;
    public Area Area => Area.Cost;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Producer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.OVERPROVISIONED.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.OVERPROVISIONED.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var load = LoadModel.Of(graph);
        if (load.IsIdle)
            return RuleResult.NotApplicable("rule.na.OVERPROVISIONED.noLoad");

        var findings = new List<RuleFinding>();

        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
        {
            if (graph.OrphanNodes.Contains(node.Id))
                continue;

            if (!NodeRoles.IsYoursToSize(node.Type))
                continue;

            var copies = NodeSettings.Instances(node);
            var keep = Keep(node, load);
            if (copies <= keep)
                continue;

            var utilization = load.UtilizationOf(node.Id);
            if (utilization >= RuleThresholds.OverprovisionedUtilization)
                continue;

            var perInstance = CostModel.PerInstance(node.Type);

            findings.Add(new RuleFinding(
                Code,
                Area,
                Severity.Warning,
                LocalizedText.Of("finding.OVERPROVISIONED.message",
                    ("node", node.Name),
                    ("copies", copies),
                    ("utilization", utilization)),

                LocalizedText.Of("finding.OVERPROVISIONED.explanation",
                    ("loadRps", Math.Round(load.LoadOf(node.Id), 2)),
                    ("keep", keep),
                    ("spare", copies - keep),
                    ("waste", (copies - keep) * perInstance),
                    ("cost", copies * perInstance)),

                Suggest(node),
                [FindingElement.Node(node.Id)]));
        }

        return RuleResult.Applicable(findings);
    }

    private static int Keep(Domain.Entities.Node node, LoadModel load)
    {
        var perInstance = CapacityModel.PerInstance(node.Type) * CapacityModel.SaturationWarning;

        var enough = perInstance <= 0m
            ? 1
            : (int)Math.Ceiling(load.LoadOf(node.Id) / perInstance);

        return Math.Max(Math.Max(enough, 1), RuleThresholds.RedundancyFloor);
    }

    private static LocalizedText Suggest(Domain.Entities.Node node) => LocalizedText.Of(node.Type switch
    {
        Domain.Enums.NodeType.Database => "finding.OVERPROVISIONED.suggestion.database",
        _ => "finding.OVERPROVISIONED.suggestion.instances"
    });
}
