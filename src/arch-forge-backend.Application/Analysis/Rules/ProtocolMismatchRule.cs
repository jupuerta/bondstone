using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

public class ProtocolMismatchRule : IArchitectureRule
{
    public string Code => RuleCodes.ProtocolMismatch;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.PROTOCOL_MISMATCH.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.PROTOCOL_MISMATCH.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var edges = graph.Connections.Where(edge => !NodeRoles.IsTelemetryEdge(graph, edge)).ToList();

        if (edges.Count == 0)
            return RuleResult.NotApplicable("rule.na.PROTOCOL_MISMATCH.noCalls");

        var findings = new List<RuleFinding>();

        foreach (var edge in edges)
        {
            if (Mismatch(graph, edge) is not { } mismatch)
                continue;

            var source = graph.NodesById.GetValueOrDefault(edge.SourceNodeId)?.Name ?? string.Empty;
            var target = graph.NodesById.GetValueOrDefault(edge.TargetNodeId)?.Name ?? string.Empty;

            findings.Add(new RuleFinding(
                Code,
                Area,
                mismatch.Severity,
                LocalizedText.Of($"finding.PROTOCOL_MISMATCH.{mismatch.Variant}.message",
                    ("source", source),
                    ("target", target)),

                LocalizedText.Of($"finding.PROTOCOL_MISMATCH.{mismatch.Variant}.explanation",
                    ("target", target)),
                LocalizedText.Of($"finding.PROTOCOL_MISMATCH.{mismatch.Variant}.suggestion"),
                [FindingElement.Connection(edge.Id)]));
        }

        return RuleResult.Applicable(findings);
    }

    private static (string Variant, Severity Severity)? Mismatch(ArchitectureGraph graph, Connection edge)
    {
        var target = graph.NodesById.GetValueOrDefault(edge.TargetNodeId)?.Type;
        var source = graph.NodesById.GetValueOrDefault(edge.SourceNodeId)?.Type;

        if (target is null || source is null)
            return null;

        return edge.Protocol switch
        {
            ProtocolType.Sql when target != NodeType.Database => ("sql", Severity.Critical),

            ProtocolType.Amqp when target is not (NodeType.Queue or NodeType.Topic)
                                   && source is not (NodeType.Queue or NodeType.Topic)
                => ("amqp", Severity.Warning),

            ProtocolType.Http when target == NodeType.Database => ("http", Severity.Warning),

            _ => null
        };
    }
}
