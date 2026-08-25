using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V4 D5.2, and the rule that finishes paying what V3 D3.5 wrote when it retired the field:
/// <em>"RateLimit is a defence against load, and there is no model of load"</em>. Now there is one,
/// so the field is back and this is what reads it.
///
/// Only applicable once traffic reaches a door, like <see cref="UndersizedComponentRule"/>: a rate
/// limit protects against a volume, and with no volume in the model there is nothing to protect
/// against and nothing honest to say.
///
/// <strong>QA §4: both doors, and the traffic that actually arrives.</strong> Until scoring v6 the
/// rule asked only gateways, because only gateways had the field - an Api that was the front door
/// with a thousand declared requests a second was a door this rule could never speak to. The field
/// now exists on the Api (<see cref="Configuration.Schemas.ApiConfiguration.RateLimit"/>), and the
/// volume is read from the load model rather than from the door's own configuration: since QA §3.1
/// the users' traffic is declared on the Client, and a door with a Client in front of it receives
/// every one of those requests whether or not it declared any itself.
/// </summary>
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
            // A door wired to nothing guards nothing, whatever traffic its configuration declares -
            // it is ORPHAN_NODE's finding, not this rule's (QA B4).
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
                // The advice is the door's (V13 D1.2): a gateway limits, an Api either limits
                // itself or gets a gateway to do it - and a Service (a door since V14 B6) has no
                // limit to set, so its only move is the gateway. Handing it the generic sentence
                // would point at a field it does not have, which is the B4 mistake over again.
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
