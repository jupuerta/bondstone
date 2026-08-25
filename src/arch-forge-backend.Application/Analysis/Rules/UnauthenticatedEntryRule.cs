using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

public class UnauthenticatedEntryRule : IArchitectureRule
{
    public string Code => RuleCodes.UnauthenticatedEntry;
    public Area Area => Area.Security;
    public Severity DefaultSeverity => Severity.Critical;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var exposed = EntryPoints.PublicFacing(graph)
            .Where(node => EntryPoints.Reaches(graph, node.Id, NodeRoles.IsSystemOfRecord))
            .ToList();

        var unguarded = EntryPoints.Unguarded(graph);

        if (exposed.Count == 0 && unguarded.Count == 0)
            return RuleResult.NotApplicable("rule.na.UNAUTHENTICATED_ENTRY.nothingExposed");

        var findings = new List<RuleFinding>();

        foreach (var node in exposed)
        {
            var authentication = NodeSettings.AuthenticationOf(node);
            var delegation = DelegationOf(graph, node);

            if (authentication == false && delegation is { } contradicted)
            {
                var provider = graph.NodesById[contradicted.TargetNodeId];

                findings.Add(new RuleFinding(
                    Code,
                    Area,
                    Severity.Warning,
                    LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.contradicted.message",
                        ("node", node.Name),
                        ("provider", provider.Name)),
                    LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.contradicted.explanation",
                        ("node", node.Name),
                        ("provider", provider.Name)),
                    LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.contradicted.suggestion",
                        ("node", node.Name),
                        ("provider", provider.Name)),
                    [FindingElement.Node(node.Id), FindingElement.Connection(contradicted.Id)]));

                continue;
            }

            if (authentication == true || delegation is not null)
                continue;

            var declared = authentication is not null;
            var variant = declared ? "open" : "undeclared";

            findings.Add(new RuleFinding(
                Code,
                Area,
                declared ? Severity.Critical : Severity.Warning,
                LocalizedText.Of($"finding.UNAUTHENTICATED_ENTRY.{variant}.message", ("node", node.Name)),

                LocalizedText.Of($"finding.UNAUTHENTICATED_ENTRY.{variant}.explanation"),
                LocalizedText.Of($"finding.UNAUTHENTICATED_ENTRY.{variant}.suggestion"),
                [FindingElement.Node(node.Id)]));
        }

        foreach (var node in unguarded)
        {
            findings.Add(new RuleFinding(
                Code,
                Area,
                Severity.Warning,
                LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.unguarded.message", ("node", node.Name)),
                LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.unguarded.explanation", ("node", node.Name)),
                LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.unguarded.suggestion", ("node", node.Name)),
                [FindingElement.Node(node.Id)]));
        }

        return RuleResult.Applicable(findings);
    }

    private static Domain.Entities.Connection? DelegationOf(ArchitectureGraph graph, Domain.Entities.Node door) =>
        graph.OutgoingBySource[door.Id].FirstOrDefault(edge =>
            graph.NodesById.TryGetValue(edge.TargetNodeId, out var target)
            && target.Type == NodeType.IdentityProvider);
}
