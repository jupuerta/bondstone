using bondstone_backend.Application.Localization;

namespace bondstone_backend.Application.Analysis.Rules;

public class OrphanNodeRule : IArchitectureRule
{
    public string Code => RuleCodes.OrphanNode;
    public Area Area => Area.Coupling;
    // V20. An unconnected box is a drawing that is not finished, not an architecture that is
    // broken: nothing can fail through it because nothing goes through it. It earns the card that
    // says so and stops charging five points for it.
    public Severity DefaultSeverity => Severity.Info;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.ORPHAN_NODE.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.ORPHAN_NODE.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        if (graph.Nodes.Count < 2)
            return RuleResult.NotApplicable("rule.na.ORPHAN_NODE.singleComponent");

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
                Fix = new AppliedFix(
                    FixKinds.RemoveNode,
                    FindingElement.Node(node.Id),
                    LocalizedText.Of("fix.ORPHAN_NODE.remove", ("node", node.Name)))
            })
            .ToList();

        return RuleResult.Applicable(findings);
    }
}
