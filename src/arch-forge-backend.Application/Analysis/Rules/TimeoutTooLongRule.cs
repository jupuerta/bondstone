using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Entities;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V15 E2 (docs/V15/QA-analisis-V15.md, M4). The twin <see cref="TimeoutTooShortRule"/> always
/// owed, and the rule <c>ConfigurationRanges.MaxTimeoutSeconds</c> said out loud it was still
/// owed: <em>"whether a timeout that is merely long still protects is a rule's question, not a
/// save's"</em>. Until v8 a <c>timeoutSeconds: 600</c> on <c>Api → Db</c> produced nothing at all
/// and the drawing scored 99, because MISSING_RESILIENCE asks only whether a timeout exists and
/// TIMEOUT_TOO_SHORT only complains about the short ones.
///
/// <strong>The threshold is not a number picked for this rule.</strong> The simulator has said
/// since V3 what a call with no timeout costs: it gives up at <see cref="LatencyModel.UnboundedWait"/>,
/// thirty seconds (D4.2). A timeout at or above that protects exactly as much as no timeout at all
/// - the caller waits what it would have waited anyway, and every other mechanism on the edge,
/// which the timeout gates (D4.2), waits with it. So the line is the model's own: a timeout is a
/// limit while it is shorter than the unbounded wait, and a wait once it is not.
///
/// <strong>Graded the way MISSING_RESILIENCE grades a missing one.</strong> A wait on a call that
/// nothing covers, into a component whose loss costs real traffic (a Critical
/// <see cref="SinglePointOfFailureFact"/>), is that outage held open on every request: Critical.
/// Anywhere else it is a Warning, because the edge does fail eventually and the fix is one field.
///
/// What it leaves alone is what every resilience rule leaves alone: the outside's requests and a
/// third party's own calls (<see cref="NodeRoles.IsNotYourCall"/>), the telemetry's scrapes
/// (<see cref="NodeRoles.IsTelemetryScrape"/>, V15 A3), and asynchronous edges, where a timeout
/// bounds nothing the caller is waiting for.
/// </summary>
public class TimeoutTooLongRule : IArchitectureRule
{
    /// <summary>
    /// What the fix lowers to when the measured call does not need more: the product's default
    /// (<c>MissingResilienceRule.FixTimeoutSeconds</c>), so "add a timeout" and "lower this one"
    /// land on the same number.
    /// </summary>
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
            return RuleResult.NotApplicable;

        var criticalSpofs = graph.Facts
            .OfType<SinglePointOfFailureFact>()
            .Where(fact => fact.Severity == Severity.Critical)
            .Select(fact => fact.NodeId)
            .ToHashSet();

        // The measured cost goes into the card next to the timeout, so the reader sees the ratio
        // rather than being told about it - and it sizes the fix. Nominal load, like the twin.
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

            // The same three conditions MISSING_RESILIENCE escalates on: nothing else covers the
            // call, and what it lands on takes real traffic down with it.
            var heldOpenOnSpof = !resilience.CircuitBreaker
                                 && !resilience.Fallback
                                 && criticalSpofs.Contains(edge.TargetNodeId);

            // Twice the measured cost, rounded up to the next whole second, and never under the
            // product's default: enough headroom that lowering it does not create a
            // TIMEOUT_TOO_SHORT of its own.
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
                // D5.6: lowering it is unambiguous, and the number is the model's.
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
