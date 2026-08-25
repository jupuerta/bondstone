using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

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
