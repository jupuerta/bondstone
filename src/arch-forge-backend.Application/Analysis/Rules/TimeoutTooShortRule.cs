using bondstone_backend.Application.Simulation;
using bondstone_backend.Domain.Entities;

using bondstone_backend.Application.Localization;

namespace bondstone_backend.Application.Analysis.Rules;

public class TimeoutTooShortRule : IArchitectureRule
{
    public string Code => RuleCodes.TimeoutTooShort;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Critical;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.TIMEOUT_TOO_SHORT.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.TIMEOUT_TOO_SHORT.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var timed = graph.Connections
            .Where(c => c.Synchronous && graph.ResilienceOf(c).TimeoutSeconds is > 0)
            .Where(c => !NodeRoles.IsNotYourCall(graph, c))
            .ToList();

        if (timed.Count == 0)
            return RuleResult.NotApplicable("rule.na.TIMEOUT_TOO_SHORT.noTimeout");

        var overloaded = graph.Facts
            .OfType<UndersizedComponentFact>()
            .Where(fact => fact.Severity == Severity.Critical)
            .Select(fact => fact.NodeId)
            .ToHashSet();

        var cost = CallCost.Of(graph, CostInputs.Nominal with { QueueingFactors = LoadModel.Of(graph).QueueingFactors });
        var findings = new List<RuleFinding>();

        foreach (var edge in timed.OrderBy(c => c.Id))
        {
            if (WaitsOnAnOverloadedComponent(graph, edge, overloaded))
                continue;

            var timeoutMs = LatencyModel.TimeoutMs(graph.ResilienceOf(edge).TimeoutSeconds!.Value);
            var callMs = cost.Of(edge).RawLatencyMs;

            if (callMs <= timeoutMs)
                continue;

            var source = graph.NodesById[edge.SourceNodeId].Name;
            var target = graph.NodesById[edge.TargetNodeId].Name;

            // No button, and the sentence starts with capacity (QA 30-08, §10.3 point 12). What makes
            // a call take longer than its own timeout is the queue in front of the destination, so
            // raising the number buys a slower answer — which is exactly what `timeout-was-enough`
            // teaches not to do: its hints say a breaker makes it fail sooner but does not make it
            // fit. Measured over the whole model, the calm-destination case does not exist: if the
            // queue is big enough to pass the timeout, whoever causes it is at its limit, and when
            // that is a box further down it is critical and `WaitsOnAnOverloadedComponent` has
            // already stood the rule down. There is nothing left for a button to do here.
            findings.Add(new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                LocalizedText.Of("finding.TIMEOUT_TOO_SHORT.expires.message",
                    ("source", source),
                    ("target", target)),
                LocalizedText.Of("finding.TIMEOUT_TOO_SHORT.expires.explanation",
                    ("target", target),
                    ("callMs", callMs),
                    ("timeoutMs", timeoutMs)),
                LocalizedText.Of("finding.TIMEOUT_TOO_SHORT.expires.suggestion",
                    ("callMs", callMs),
                    ("target", target)),
                [FindingElement.Connection(edge.Id)]));
        }

        return RuleResult.Applicable(findings);
    }

    private static bool WaitsOnAnOverloadedComponent(ArchitectureGraph graph, Connection edge, IReadOnlySet<Guid> overloaded)
    {
        if (overloaded.Count == 0)
            return false;

        var visited = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(edge.TargetNodeId);

        while (pending.Count > 0)
        {
            var nodeId = pending.Pop();

            if (!visited.Add(nodeId))
                continue;

            if (overloaded.Contains(nodeId))
                return true;

            foreach (var next in graph.OutgoingBySource[nodeId].Where(c => c.Synchronous))
                pending.Push(next.TargetNodeId);
        }

        return false;
    }
}
