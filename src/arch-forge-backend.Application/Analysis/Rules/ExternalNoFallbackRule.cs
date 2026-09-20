using bondstone_backend.Application.Localization;
using bondstone_backend.Domain.Enums;

namespace bondstone_backend.Application.Analysis.Rules;

public class ExternalNoFallbackRule : IArchitectureRule
{
    private const int FixTimeoutSeconds = 5;

    public string Code => RuleCodes.ExternalNoFallback;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Critical;
    public RulePhase Phase => RulePhase.Producer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.EXTERNAL_NO_FALLBACK.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.EXTERNAL_NO_FALLBACK.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var calls = graph.Connections
            .Where(c => c.Synchronous)
            .Where(c => !NodeRoles.IsNotYourCall(graph, c))
            .Where(c => graph.NodesById.TryGetValue(c.TargetNodeId, out var target) && target.Type == NodeType.ExternalService)
            .ToList();

        if (calls.Count == 0)
            return RuleResult.NotApplicable("rule.na.EXTERNAL_NO_FALLBACK.noExternalCall");

        var findings = new List<RuleFinding>();

        foreach (var edge in calls.Where(c => graph.ResilienceOf(c) is { CircuitBreaker: false, Fallback: false }).OrderBy(c => c.Id))
        {
            graph.Facts.Add(new ExternalNoFallbackFact(edge.Id));

            var hasTimeout = graph.ResilienceOf(edge).TimeoutSeconds is not null;
            var source = graph.NodesById[edge.SourceNodeId].Name;
            var target = graph.NodesById[edge.TargetNodeId].Name;

            var variant = hasTimeout ? "unprotected" : "noTimeout";

            findings.Add(new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                LocalizedText.Of("finding.EXTERNAL_NO_FALLBACK.unprotected.message",
                    ("source", source),
                    ("target", target)),
                LocalizedText.Of($"finding.EXTERNAL_NO_FALLBACK.{variant}.explanation",
                    ("source", source),
                    ("target", target)),
                LocalizedText.Of($"finding.EXTERNAL_NO_FALLBACK.{variant}.suggestion"),
                [FindingElement.Connection(edge.Id)])
            {
                Fix = new AppliedFix(
                    FixKinds.SetEdgeResilience,
                    FindingElement.Connection(edge.Id),
                    hasTimeout
                        ? LocalizedText.Of("fix.EXTERNAL_NO_FALLBACK.addFallback")
                        : LocalizedText.Of("fix.EXTERNAL_NO_FALLBACK.timeoutAndFallback", ("seconds", FixTimeoutSeconds)),
                    TimeoutSeconds: hasTimeout ? null : FixTimeoutSeconds,
                    Fallback: true)
            });
        }

        return RuleResult.Applicable(findings);
    }
}
