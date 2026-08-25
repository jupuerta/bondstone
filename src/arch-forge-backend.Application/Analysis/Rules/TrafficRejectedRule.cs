using arch_forge_backend.Domain.Entities;

using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Analysis.Rules;

public class TrafficRejectedRule : IArchitectureRule
{
    public string Code => RuleCodes.TrafficRejected;
    public Area Area => Area.Availability;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.TRAFFIC_REJECTED.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.TRAFFIC_REJECTED.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var load = LoadModel.Of(graph);
        if (load.IsIdle)
            return RuleResult.NotApplicable("rule.na.TRAFFIC_REJECTED.noLoad");

        var limited = graph.Nodes
            .Where(node => NodeSettings.RateLimitOf(node) is > 0 && !graph.OrphanNodes.Contains(node.Id))
            .OrderBy(node => node.Id)
            .ToList();

        if (limited.Count == 0)
            return RuleResult.NotApplicable("rule.na.TRAFFIC_REJECTED.noLimit");

        var findings = new List<RuleFinding>();

        foreach (var door in limited)
        {
            var demand = load.DemandOf(door.Id);
            var admitted = load.LoadOf(door.Id);

            if (demand <= 0m)
                continue;

            var rejected = (demand - admitted) / demand;
            if (rejected < RuleThresholds.RejectedTrafficWarning)
                continue;

            findings.Add(new RuleFinding(
                Code,
                Area,
                rejected >= RuleThresholds.RejectedTrafficCritical ? Severity.Critical : Severity.Warning,
                LocalizedText.Of("finding.TRAFFIC_REJECTED.shedding.message",
                    ("node", door.Name),
                    ("rejected", rejected)),
                LocalizedText.Of("finding.TRAFFIC_REJECTED.shedding.explanation",
                    ("node", door.Name),
                    ("demandRps", demand),
                    ("admittedRps", admitted)),
                LocalizedText.Of("finding.TRAFFIC_REJECTED.shedding.suggestion"),
                [FindingElement.Node(door.Id)]));
        }

        return RuleResult.Applicable(findings);
    }
}
