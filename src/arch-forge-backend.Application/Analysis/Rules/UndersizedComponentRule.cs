using arch_forge_backend.Application.Simulation;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

public class UndersizedComponentRule : IArchitectureRule
{
    public string Code => RuleCodes.UndersizedComponent;
    public Area Area => Area.Scalability;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Producer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.UNDERSIZED_COMPONENT.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.UNDERSIZED_COMPONENT.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var load = LoadModel.Of(graph);
        if (load.IsIdle)
            return RuleResult.NotApplicable("rule.na.UNDERSIZED_COMPONENT.noLoad");

        var findings = new List<RuleFinding>();

        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
        {
            if (graph.OrphanNodes.Contains(node.Id))
                continue;

            if (!NodeRoles.IsYoursToSize(node.Type))
                continue;

            var utilization = load.UtilizationOf(node.Id);
            if (utilization < CapacityModel.SaturationWarning)
                continue;

            var overloaded = utilization > 1m;
            var severity = overloaded ? Severity.Critical : Severity.Warning;

            graph.Facts.Add(new UndersizedComponentFact(node.Id, severity));

            var writesBind = load.WritesBind(node.Id);
            var (loadRps, capacityRps) = load.BindingOf(node.Id);

            findings.Add(new RuleFinding(
                Code,
                Area,
                severity,
                LocalizedText.Of(overloaded
                        ? "finding.UNDERSIZED_COMPONENT.overloaded.message"
                        : "finding.UNDERSIZED_COMPONENT.atCapacity.message",
                    ("node", node.Name)),

                LocalizedText.Of(writesBind
                        ? (overloaded
                            ? "finding.UNDERSIZED_COMPONENT.overloaded.explanation.writes"
                            : "finding.UNDERSIZED_COMPONENT.atCapacity.explanation.writes")
                        : Explanation(graph, node, overloaded),
                    ("loadRps", loadRps),
                    ("capacityRps", capacityRps),
                    ("utilization", utilization)),

                writesBind && node.Type == Domain.Enums.NodeType.Database
                    ? LocalizedText.Of("finding.UNDERSIZED_COMPONENT.suggestion.databaseWrites")
                    : Suggest(graph, node),
                [FindingElement.Node(node.Id)]));
        }

        return RuleResult.Applicable(findings);
    }

    private static string Explanation(ArchitectureGraph graph, Domain.Entities.Node node, bool overloaded)
    {
        if (!overloaded)
            return "finding.UNDERSIZED_COMPONENT.atCapacity.explanation";

        var hasDependencies = graph.OutgoingBySource[node.Id]
            .Any(edge => graph.NodesById[edge.TargetNodeId].Type != Domain.Enums.NodeType.Observability);

        return hasDependencies
            ? "finding.UNDERSIZED_COMPONENT.overloaded.explanation.withDependencies"
            : "finding.UNDERSIZED_COMPONENT.overloaded.explanation";
    }

    private static LocalizedText Suggest(ArchitectureGraph graph, Domain.Entities.Node node) => LocalizedText.Of(node.Type switch
    {
        Domain.Enums.NodeType.Database => "finding.UNDERSIZED_COMPONENT.suggestion.database",
        Domain.Enums.NodeType.Queue => "finding.UNDERSIZED_COMPONENT.suggestion.queue",
        _ when graph.IncomingByTarget[node.Id].Any(c => c.Synchronous) => "finding.UNDERSIZED_COMPONENT.suggestion.shareWork",
        _ => "finding.UNDERSIZED_COMPONENT.suggestion.addInstances"
    });
}
