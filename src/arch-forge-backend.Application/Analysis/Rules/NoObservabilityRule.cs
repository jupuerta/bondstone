using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V4 D5.2 and D5.3. The rule that makes <see cref="Area.Observability"/> - empty in the contract
/// since V2 - mean something, and the reason the palette had to grow first: <strong>without a
/// component to point at there is no rule, and without a rule there is no area</strong>. It was the
/// longest chain in V4, and it is walked whole or not at all.
///
/// It asks the only question the model can answer honestly: is anything watching. Not what it
/// collects, not how - the model has no notion of either.
/// </summary>
public class NoObservabilityRule : IArchitectureRule
{
    public string Code => RuleCodes.NoObservability;
    public Area Area => Area.Observability;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.NO_OBSERVABILITY.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.NO_OBSERVABILITY.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        // A single component is a sketch, not an architecture: asking it for telemetry would be
        // noise on the first thing anybody drags onto the canvas.
        if (graph.Nodes.Count < 2)
            return RuleResult.NotApplicable;

        var connected = graph.Nodes
            .Where(n => n.Type == NodeType.Observability)
            .Where(n => graph.IncomingByTarget[n.Id].Any() || graph.OutgoingBySource[n.Id].Any())
            .ToList();

        if (connected.Count > 0)
            return RuleResult.Applicable([]);

        var hasOrphan = graph.Nodes.Any(n => n.Type == NodeType.Observability);

        return RuleResult.Applicable(new RuleFinding(
            Code,
            Area,
            DefaultSeverity,
            // Two keys and not one with a flag in it: the two sentences say different things -
            // one is "you wired nothing to it", the other "there is nothing" - and which of them
            // applies is the rule's decision, not the translator's (V7 D1.1).
            LocalizedText.Of(hasOrphan
                ? "finding.NO_OBSERVABILITY.orphan.message"
                : "finding.NO_OBSERVABILITY.absent.message"),
            LocalizedText.Of("finding.NO_OBSERVABILITY.explanation"),
            LocalizedText.Of("finding.NO_OBSERVABILITY.suggestion"),
            connected.Concat(graph.Nodes.Where(n => n.Type == NodeType.Observability))
                .Select(n => FindingElement.Node(n.Id))
                .ToList()));
    }
}
