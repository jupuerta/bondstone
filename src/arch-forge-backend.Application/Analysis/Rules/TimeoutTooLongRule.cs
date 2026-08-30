using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Entities;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

public class TimeoutTooLongRule : IArchitectureRule
{
    private const int FixTimeoutSeconds = 5;

    public string Code => RuleCodes.TimeoutTooLong;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.TIMEOUT_TOO_LONG.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.TIMEOUT_TOO_LONG.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var timed = graph.Connections
            .Where(c => c.Synchronous && graph.ResilienceOf(c).TimeoutSeconds is > 0)
            .Where(c => !NodeRoles.IsNotYourCall(graph, c) && !NodeRoles.IsTelemetryScrape(graph, c))
            .ToList();

        if (timed.Count == 0)
            return RuleResult.NotApplicable("rule.na.TIMEOUT_TOO_LONG.noTimeout");

        var criticalSpofs = graph.Facts
            .OfType<SinglePointOfFailureFact>()
            .Where(fact => fact.Severity == Severity.Critical)
            .Select(fact => fact.NodeId)
            .ToHashSet();

        var cost = CallCost.Of(graph, CostInputs.Nominal with { QueueingFactors = LoadModel.Of(graph).QueueingFactors });
        var findings = new List<RuleFinding>();

        foreach (var edge in timed.OrderBy(c => c.Id))
        {
            var resilience = graph.ResilienceOf(edge);
            var timeoutMs = LatencyModel.TimeoutMs(resilience.TimeoutSeconds!.Value);

            if (timeoutMs < RuleThresholds.TimeoutUnboundedMs)
                continue;

            var callMs = cost.Of(edge).RawLatencyMs;
            var source = graph.NodesById[edge.SourceNodeId].Name;
            var target = graph.NodesById[edge.TargetNodeId].Name;

            // Same reading as the simulator: a fallback a router or a write cannot honour does not
            // hold the call open any less (QA 30-08, §10.2 point 2).
            var heldOpenOnSpof = !resilience.CircuitBreaker
                                 && !graph.BelievesFallback(edge)
                                 && criticalSpofs.Contains(edge.TargetNodeId);

            var suggested = Math.Max(FixTimeoutSeconds, (int)Math.Ceiling(callMs * 2 / 1000m));

            findings.Add(new RuleFinding(
                Code,
                Area,
                heldOpenOnSpof ? Severity.Critical : Severity.Warning,
                LocalizedText.Of("finding.TIMEOUT_TOO_LONG.unbounded.message",
                    ("source", source),
                    ("target", target),
                    ("timeoutMs", timeoutMs)),
                LocalizedText.Of(heldOpenOnSpof
                        ? "finding.TIMEOUT_TOO_LONG.unboundedToSpof.explanation"
                        : "finding.TIMEOUT_TOO_LONG.unbounded.explanation",
                    ("target", target),
                    ("callMs", callMs),
                    ("timeoutMs", timeoutMs),
                    ("waitMs", RuleThresholds.TimeoutUnboundedMs)),
                LocalizedText.Of("finding.TIMEOUT_TOO_LONG.unbounded.suggestion", ("callMs", callMs)),
                [FindingElement.Connection(edge.Id)])
            {
                Fix = new AppliedFix(
                    FixKinds.SetEdgeResilience,
                    FindingElement.Connection(edge.Id),
                    LocalizedText.Of("fix.TIMEOUT_TOO_LONG.lower", ("seconds", suggested)),
                    TimeoutSeconds: suggested)
            });
        }

        return RuleResult.Applicable(findings);
    }
}
