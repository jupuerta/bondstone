using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V4 D5.2, a debt V2 wrote down and V3 pushed twice. A component nobody calls and that calls
/// nobody is not part of the architecture: it is either a leftover from a draft or the thing you
/// forgot to wire, and both are worth saying out loud before anything else is analysed - every
/// other rule will simply ignore it.
/// </summary>
public class OrphanNodeRule : IArchitectureRule
{
    public string Code => RuleCodes.OrphanNode;
    public Area Area => Area.Coupling;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.ORPHAN_NODE.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.ORPHAN_NODE.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        // With a single component there is nothing to be disconnected from: a one-box architecture
        // is unfinished, not wrong, and saying so on the first thing anybody drags in would make
        // the analyzer noise from the very first second.
        if (graph.Nodes.Count < 2)
            return RuleResult.NotApplicable;

        // The set lives on the graph (QA B4) because this rule's promise - "every other rule will
        // simply ignore it" - needs the other rules to agree on what an orphan is.
        var findings = graph.Nodes
            .Where(node => graph.OrphanNodes.Contains(node.Id))
            .OrderBy(node => node.Id)
            .Select(node => new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                LocalizedText.Of("finding.ORPHAN_NODE.disconnected.message", ("node", node.Name)),
                LocalizedText.Of("finding.ORPHAN_NODE.disconnected.explanation"),
                LocalizedText.Of("finding.ORPHAN_NODE.disconnected.suggestion"),
                [FindingElement.Node(node.Id)])
            {
                // D5.6. Only one of the two ways out is automatable: *deleting* it is unambiguous,
                // *wiring* it is the decision - to what, and in which direction - so the suggestion
                // offers both and the button only does the one that has a single answer.
                Fix = new AppliedFix(
                    FixKinds.RemoveNode,
                    FindingElement.Node(node.Id),
                    LocalizedText.Of("fix.ORPHAN_NODE.remove", ("node", node.Name)))
            })
            .ToList();

        return RuleResult.Applicable(findings);
    }
}
