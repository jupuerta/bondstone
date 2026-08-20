using arch_forge_backend.Application.Simulation;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V4 D5.2 and bloque DT. <strong>The MISSING_CACHE of traffic</strong>: the rule that turns the
/// new axis into advice before the user simulates anything.
///
/// It only has something to say once somebody declares how much traffic comes in the front door
/// (DT.1), and that is deliberate - with no declared load there is no utilisation, so there is
/// nothing to be under-sized for. Every architecture drawn before V4 sees this rule as
/// "not applicable", which is the truth rather than a courtesy.
/// </summary>
public class UndersizedComponentRule : IArchitectureRule
{
    public string Code => RuleCodes.UndersizedComponent;
    public Area Area => Area.Scalability;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.UNDERSIZED_COMPONENT.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.UNDERSIZED_COMPONENT.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var load = LoadModel.Of(graph);
        if (load.IsIdle)
            return RuleResult.NotApplicable;

        var findings = new List<RuleFinding>();

        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
        {
            var utilization = load.UtilizationOf(node.Id);
            if (utilization < CapacityModel.SaturationWarning)
                continue;

            // Over capacity it is not "close to the limit", it is already rejecting requests, and
            // the two deserve different colours: one is a risk, the other is an outage in progress.
            var overloaded = utilization > 1m;

            findings.Add(new RuleFinding(
                Code,
                Area,
                overloaded ? Severity.Critical : Severity.Warning,
                LocalizedText.Of(overloaded
                        ? "finding.UNDERSIZED_COMPONENT.overloaded.message"
                        : "finding.UNDERSIZED_COMPONENT.atCapacity.message",
                    ("node", node.Name)),

                // Two whole sentences, not a shared prefix plus a tail. Concatenating fragments is
                // the localization bug that survives review: the join reads fine in the language it
                // was written in and falls apart in the next one, where the clause order moves
                // (V7 D1.1). The numbers travel raw and the catalogue formats them.
                LocalizedText.Of(overloaded
                        ? "finding.UNDERSIZED_COMPONENT.overloaded.explanation"
                        : "finding.UNDERSIZED_COMPONENT.atCapacity.explanation",
                    ("loadRps", load.LoadOf(node.Id)),
                    ("capacityRps", load.CapacityOf(node.Id)),
                    ("utilization", utilization)),

                Suggest(graph, node),
                [FindingElement.Node(node.Id)]));
        }

        return RuleResult.Applicable(findings);
    }

    private static LocalizedText Suggest(ArchitectureGraph graph, Domain.Entities.Node node) => LocalizedText.Of(node.Type switch
    {
        Domain.Enums.NodeType.Database => "finding.UNDERSIZED_COMPONENT.suggestion.database",
        Domain.Enums.NodeType.Queue => "finding.UNDERSIZED_COMPONENT.suggestion.queue",
        _ when graph.IncomingByTarget[node.Id].Any(c => c.Synchronous) => "finding.UNDERSIZED_COMPONENT.suggestion.shareWork",
        _ => "finding.UNDERSIZED_COMPONENT.suggestion.addInstances"
    });
}
