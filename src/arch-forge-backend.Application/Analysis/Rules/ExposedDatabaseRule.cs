using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

public class ExposedDatabaseRule : IArchitectureRule
{
    public string Code => RuleCodes.ExposedDatabase;
    public Area Area => Area.Security;
    public Severity DefaultSeverity => Severity.Critical;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.EXPOSED_DATABASE.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.EXPOSED_DATABASE.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var stores = graph.Nodes
            .Where(n => NodeRoles.IsSystemOfRecord(n.Type) && !graph.OrphanNodes.Contains(n.Id))
            .ToList();

        if (stores.Count == 0)
            return RuleResult.NotApplicable("rule.na.EXPOSED_DATABASE.noStore");

        var frontier = EntryPoints.Frontier(graph).Select(n => n.Id).ToHashSet();

        var bare = EntryPoints.Frontier(graph, type => NodeRoles.CanAuthenticate(type) || type == NodeType.Cdn)
            .Select(n => n.Id)
            .ToHashSet();

        var findings = stores
            .Where(store => store.Type == NodeType.ObjectStorage ? bare.Contains(store.Id) : frontier.Contains(store.Id))
            .OrderBy(store => store.Id)
            .Select(store => new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                LocalizedText.Of("finding.EXPOSED_DATABASE.reachable.message", ("node", store.Name)),
                LocalizedText.Of("finding.EXPOSED_DATABASE.reachable.explanation"),
                LocalizedText.Of("finding.EXPOSED_DATABASE.reachable.suggestion"),
                [FindingElement.Node(store.Id)]))
            .ToList();

        return RuleResult.Applicable(findings);
    }
}
