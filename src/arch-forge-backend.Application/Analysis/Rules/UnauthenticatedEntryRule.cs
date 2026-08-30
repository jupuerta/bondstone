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

        // A door nobody calls is not asked: two boxes and an arrow are a sketch, not an open door
        // (V13 D2.1, QA 30-08 §10.2). What calls a door is traffic that reaches it — declared on it
        // or upstream — or a drawn client, number or not; the same honesty NoRateLimitRule has had
        // since V4. Declared-no and undeclared stand down together, so silence never pays.
        var load = LoadModel.Of(graph);
        var drawnClient = graph.Nodes.Any(n => NodeRoles.IsTrafficOrigin(n.Type));

        var findings = new List<RuleFinding>();
        var answered = 0;

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
            {
                answered++;
                continue;
            }

            if (!drawnClient && load.LoadOf(node.Id) <= 0)
                continue;

            // Declaring the door open used to cost a critical and saying nothing a warning, so the
            // honest drawing scored worse than the silent one (QA 30-08, §4.3). A door nobody has
            // vouched for is open until proven otherwise: same weight, different sentence.
            var variant = authentication is not null ? "open" : "undeclared";

            findings.Add(new RuleFinding(
                Code,
                Area,
                Severity.Critical,
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

        if (findings.Count == 0 && answered == 0)
            return RuleResult.NotApplicable("rule.na.UNAUTHENTICATED_ENTRY.noTrafficAtDoor");

        return RuleResult.Applicable(findings);
    }

    private static Domain.Entities.Connection? DelegationOf(ArchitectureGraph graph, Domain.Entities.Node door) =>
        graph.OutgoingBySource[door.Id].FirstOrDefault(edge =>
            graph.NodesById.TryGetValue(edge.TargetNodeId, out var target)
            && target.Type == NodeType.IdentityProvider);
}
