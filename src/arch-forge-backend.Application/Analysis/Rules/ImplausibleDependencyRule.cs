using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

public class ImplausibleDependencyRule : IArchitectureRule
{
    public string Code => RuleCodes.ImplausibleDependency;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.IMPLAUSIBLE_DEPENDENCY.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.IMPLAUSIBLE_DEPENDENCY.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var edges = graph.Connections
            .Where(edge => !NodeRoles.IsTelemetryEdge(graph, edge) && !NodeRoles.IsCallByThirdParty(graph, edge))
            .ToList();

        if (edges.Count == 0)
            return RuleResult.NotApplicable("rule.na.IMPLAUSIBLE_DEPENDENCY.noCalls");

        var findings = new List<RuleFinding>();

        foreach (var edge in edges.OrderBy(edge => edge.Id))
        {
            if (Implausible(graph, edge) is not { } mismatch)
                continue;

            var source = graph.NodesById[edge.SourceNodeId].Name;
            var target = graph.NodesById[edge.TargetNodeId].Name;

            findings.Add(new RuleFinding(
                Code,
                Area,
                mismatch.Severity,
                LocalizedText.Of($"finding.IMPLAUSIBLE_DEPENDENCY.{mismatch.Variant}.message",
                    ("source", source),
                    ("target", target)),

                LocalizedText.Of($"finding.IMPLAUSIBLE_DEPENDENCY.{mismatch.Variant}.explanation",
                    ("source", source),
                    ("target", target)),
                LocalizedText.Of($"finding.IMPLAUSIBLE_DEPENDENCY.{mismatch.Variant}.suggestion",
                    ("source", source),
                    ("target", target)),
                [FindingElement.Connection(edge.Id)]));
        }

        return RuleResult.Applicable(findings);
    }

    private static (string Variant, Severity Severity)? Implausible(ArchitectureGraph graph, Connection edge)
    {
        if (!graph.NodesById.TryGetValue(edge.SourceNodeId, out var source)
            || !graph.NodesById.TryGetValue(edge.TargetNodeId, out var target))
            return null;

        if (source.Id == target.Id)
            return null;

        if (NodeRoles.IsSystemOfRecord(source.Type))
            return ("store", Severity.Warning);

        if (NodeRoles.IsTrafficOrigin(target.Type))
            return ("client", Severity.Warning);

        if (source.Type == NodeType.Cdn && !IsOrigin(target.Type))
            return ("cdn", Severity.Critical);

        if (source.Type == NodeType.Scheduler && !IsWork(target.Type))
            return ("scheduler", Severity.Warning);

        return null;
    }

    private static bool IsOrigin(NodeType type) =>
        type is NodeType.Api or NodeType.Gateway or NodeType.LoadBalancer
             or NodeType.Service or NodeType.ObjectStorage or NodeType.ExternalService;

    private static bool IsWork(NodeType type) =>
        type is NodeType.Worker or NodeType.Service or NodeType.Api
             or NodeType.Queue or NodeType.Topic or NodeType.Gateway or NodeType.ExternalService;
}
