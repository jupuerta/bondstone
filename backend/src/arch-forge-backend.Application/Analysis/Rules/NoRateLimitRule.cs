using bondstone_backend.Application.Simulation;
using bondstone_backend.Domain.Enums;

using bondstone_backend.Application.Localization;

namespace bondstone_backend.Application.Analysis.Rules;

public class NoRateLimitRule : IArchitectureRule
{
    public string Code => RuleCodes.NoRateLimit;
    public Area Area => Area.Security;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.NO_RATE_LIMIT.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.NO_RATE_LIMIT.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var load = LoadModel.Of(graph);

        var doors = EntryPoints.PublicFacing(graph)
            .Where(n => !graph.OrphanNodes.Contains(n.Id))
            .Where(n => load.LoadOf(n.Id) > 0)
            .ToList();

        if (doors.Count == 0)
            return RuleResult.NotApplicable("rule.na.NO_RATE_LIMIT.noTrafficAtDoor");

        var findings = doors
            .Where(door => NodeSettings.RateLimitOf(door) is not > 0)
            .Select(door => new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                LocalizedText.Of("finding.NO_RATE_LIMIT.unlimited.message", ("node", door.Name)),
                LocalizedText.Of("finding.NO_RATE_LIMIT.unlimited.explanation"),
                LocalizedText.Of(door.Type switch
                {
                    NodeType.Api => "finding.NO_RATE_LIMIT.unlimited.suggestion.api",
                    NodeType.Service => "finding.NO_RATE_LIMIT.unlimited.suggestion.service",
                    _ => "finding.NO_RATE_LIMIT.unlimited.suggestion"
                }),
                [FindingElement.Node(door.Id)]))
            .ToList();

        return RuleResult.Applicable(findings);
    }
}
