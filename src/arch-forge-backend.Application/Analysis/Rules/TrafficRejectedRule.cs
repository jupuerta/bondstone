using arch_forge_backend.Domain.Entities;

using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V14 §4.3. The sweep's C4: a Client declaring a million requests a second in front of a
/// <c>rateLimit: 100</c>, and everything behind the door came out perfect - 98, "solid". The
/// limit had converted "your system cannot serve your users" into "your system is healthy", and
/// the analyzer never said that 99,99 % of the declared demand was being turned away at the door.
///
/// <strong>The lesson is that limiting is not scaling.</strong> A rate limit protects what is
/// behind it; it serves nobody extra. Below half the demand it is a limit doing its job - shedding
/// a peak is exactly what NO_RATE_LIMIT asks the door to be able to do, and flagging that would be
/// the two rules contradicting each other. From half up, the limit is standing in for capacity,
/// and from nine tenths the drawing says "we turn away nearly everyone", which is not a
/// configuration nuance - it is the availability of the system, told from the door.
///
/// <strong>Availability, not Scalability</strong>, and that is half the point of the rule (V14
/// B13): the users the door turns away are requests the system does not serve, which is an
/// availability statement - and the area had exactly one rule while Resilience had eight.
///
/// The numbers travel raw (demand, admitted, the fraction) so the card can be redone by hand,
/// which is the property every figure in this product owes (D3.1).
/// </summary>
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
            return RuleResult.NotApplicable;

        // Only doors with a limit have anything to reject; a door without one lets everything
        // through, and what happens then is UNDERSIZED_COMPONENT's story.
        var limited = graph.Nodes
            .Where(node => NodeSettings.RateLimitOf(node) is > 0 && !graph.OrphanNodes.Contains(node.Id))
            .OrderBy(node => node.Id)
            .ToList();

        if (limited.Count == 0)
            return RuleResult.NotApplicable;

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
