using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

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
        if (graph.Nodes.Count < 2)
            return RuleResult.NotApplicable("rule.na.NO_OBSERVABILITY.singleComponent");

        var observable = graph.Nodes
            .Where(n => NodeRoles.IsObservable(n.Type) && !graph.OrphanNodes.Contains(n.Id))
            .OrderBy(n => n.Id)
            .ToList();

        if (observable.Count == 0)
            return RuleResult.NotApplicable("rule.na.NO_OBSERVABILITY.nothingToWatch");

        var telemetry = graph.Nodes.Where(n => NodeRoles.IsTelemetry(n.Type)).OrderBy(n => n.Id).ToList();

        var theOnlyTelemetry = telemetry.Count == 1 ? telemetry[0] : null;

        var unobserved = observable.Where(node => !IsObserved(graph, node)).ToList();

        var blind = unobserved.Count == observable.Count && observable.Count >= RuleThresholds.SyncChainWarning;

        var findings = new List<RuleFinding>();

        if (blind)
            findings.Add(Blind(unobserved, telemetry, theOnlyTelemetry));

        findings.AddRange(unobserved.Select(node => Report(node, telemetry.Count, theOnlyTelemetry)));

        return RuleResult.Applicable(findings);
    }

    private RuleFinding Blind(IReadOnlyList<Node> unobserved, IReadOnlyList<Node> telemetry, Node? theOnlyTelemetry)
    {
        var suggestion = theOnlyTelemetry is not null
            ? LocalizedText.Of("finding.NO_OBSERVABILITY.blind.suggestion.present", ("telemetry", theOnlyTelemetry.Name))
            : LocalizedText.Of(telemetry.Count == 0
                ? "finding.NO_OBSERVABILITY.blind.suggestion.absent"
                : "finding.NO_OBSERVABILITY.blind.suggestion.several");

        return new RuleFinding(
            Code,
            Area,
            Severity.Critical,
            LocalizedText.Of("finding.NO_OBSERVABILITY.blind.message", ("count", unobserved.Count)),
            LocalizedText.Of("finding.NO_OBSERVABILITY.blind.explanation"),
            suggestion,
            unobserved.Select(node => FindingElement.Node(node.Id)).ToList());
    }

    private static bool IsObserved(ArchitectureGraph graph, Node node) =>
        graph.IncomingByTarget[node.Id].Select(c => c.SourceNodeId)
            .Concat(graph.OutgoingBySource[node.Id].Select(c => c.TargetNodeId))
            .Any(otherId => graph.NodesById.TryGetValue(otherId, out var other) && NodeRoles.IsTelemetry(other.Type));

    private RuleFinding Report(Node node, int telemetryCount, Node? theOnlyTelemetry)
    {
        var suggestion = theOnlyTelemetry is not null
            ? LocalizedText.Of("finding.NO_OBSERVABILITY.unobserved.suggestion.present",
                ("node", node.Name),
                ("telemetry", theOnlyTelemetry.Name))
            : LocalizedText.Of(telemetryCount == 0
                    ? "finding.NO_OBSERVABILITY.unobserved.suggestion.absent"
                    : "finding.NO_OBSERVABILITY.unobserved.suggestion.several",
                ("node", node.Name));

        return new RuleFinding(
            Code,
            Area,
            Severity.Warning,
            LocalizedText.Of("finding.NO_OBSERVABILITY.unobserved.message", ("node", node.Name)),
            LocalizedText.Of("finding.NO_OBSERVABILITY.unobserved.explanation", ("node", node.Name)),
            suggestion,
            [FindingElement.Node(node.Id)])
        {
            Fix = theOnlyTelemetry is null
                ? null
                : new AppliedFix(
                    FixKinds.ConnectTo,
                    FindingElement.Node(node.Id),
                    LocalizedText.Of("fix.NO_OBSERVABILITY.connect", ("telemetry", theOnlyTelemetry.Name)),
                    TargetNodeId: theOnlyTelemetry.Id)
        };
    }
}
