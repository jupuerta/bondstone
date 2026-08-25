using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Entities;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V4 D5.2 - "timeout inconsistencies", the third of the debts V2 wrote down.
///
/// <strong>It was impossible in V2 and it is trivial now</strong>, and that is worth saying: it
/// needs to know what a call costs, and that is exactly <see cref="CallCost"/>, which V3 wrote and
/// tested for the simulator. The debt was not deferred out of laziness - it could not be done.
///
/// A timeout below what the call takes when everything is <em>working</em>, under the traffic that
/// was declared, does not protect anything: it expires every single time, so the dependency behaves as if it were permanently
/// down, retries pile onto a component that is answering fine, and the fallback - if there is one -
/// becomes the normal path. It is the one misconfiguration that turns a healthy architecture into
/// a broken one with no component failing.
///
/// V15 A2 (docs/V15/QA-analisis-V15.md, C7). <strong>And past 100 % the figure is somebody
/// else's.</strong> Traffic is what makes a call slow, and this rule was written for it - but over
/// capacity the latency <see cref="CallCost"/> computes is not the call, it is the outage.
/// <c>Users(50.000 rps) → Api → Db</c> with a 3 s timeout read <em>"it costs 33,34 s with
/// everything working"</em> two cards above the two UNDERSIZED_COMPONENT criticals saying the
/// opposite, and its guided fix was a 67 s timeout: the one piece of advice in the panel that made
/// the system worse when followed, with Resilience paying 15 points for what Scalability had
/// already charged. So this is a Consumer of <see cref="UndersizedComponentFact"/> now, the way
/// MISSING_RESILIENCE consumes SPOF: over a call that lands on - or waits on - a component marked
/// as overloaded, the number is Scalability's and this rule says nothing. Below the line it keeps
/// reading it: a component at 95 % answers at twenty times its idle cost and is still answering,
/// and a timeout shorter than that is exactly this rule's lesson.
/// </summary>
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
            // The outside's timeout is not the architect's knob (QA §3.1), and neither is the
            // one a provider sets on its own calls (V14 B10).
            .Where(c => !NodeRoles.IsNotYourCall(graph, c))
            .ToList();

        if (timed.Count == 0)
            return RuleResult.NotApplicable("rule.na.TIMEOUT_TOO_SHORT.noTimeout");

        // V15 A2. Where UNDERSIZED_COMPONENT has already said "it is drowning", the cost of a call
        // into it is that finding's number, not this rule's. Critical only: at capacity (Warning)
        // the component is slower and still answering, which is the case this rule exists for.
        // Filtered after the applicability check, like MISSING_RESILIENCE over the edges other
        // rules covered: the check was made, and the timeout came out doing its job - firing
        // because something is genuinely wrong, and that something has its own card.
        var overloaded = graph.Facts
            .OfType<UndersizedComponentFact>()
            .Where(fact => fact.Severity == Severity.Critical)
            .Select(fact => fact.NodeId)
            .ToHashSet();

        // Nothing broken and nothing injected - but **under the declared load** (DT.6), and that is
        // what makes this rule worth having rather than dead code. Timeouts are configured in whole
        // seconds and an idle architecture answers in tens of milliseconds, so a timeout shorter
        // than an idle call is nearly unreachable. Traffic is what makes calls slow: at 95 % of its
        // capacity every hop costs twenty times its idle self (CapacityModel.QueueingFactor), and a
        // short chain of them crosses a whole second with every component still answering.
        //
        // It is the same walk the simulator does, so the figure in the message is the figure the
        // impact panel shows.
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

            // Twice the measured cost, rounded up to the next whole second: enough headroom that
            // the fix does not have to be applied twice, and still a timeout rather than a wait.
            var suggested = Math.Max(1, (int)Math.Ceiling(callMs * 2 / 1000m));

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
                LocalizedText.Of("finding.TIMEOUT_TOO_SHORT.expires.suggestion", ("callMs", callMs)),
                [FindingElement.Connection(edge.Id)])
            {
                // D5.6: raising it is unambiguous. *Shortening the call* is the other fix and it is
                // a decision - which one of the hops goes - so it stays as text.
                Fix = new AppliedFix(
                    FixKinds.SetEdgeResilience,
                    FindingElement.Connection(edge.Id),
                    LocalizedText.Of("fix.TIMEOUT_TOO_SHORT.raise", ("seconds", suggested)),
                    TimeoutSeconds: suggested)
            });
        }

        return RuleResult.Applicable(findings);
    }

    /// <summary>
    /// Whether the call lands on, or waits on, a component marked as overloaded. Not only the
    /// target: <see cref="CallCost"/> charges a caller the whole synchronous subtree under it, so
    /// a database drowning two hops down shows up in this edge's cost just the same - raw, when
    /// the hop in between has no timeout (which is how the canvas draws an edge), or as that hop's
    /// expired timeout when it has one. Either way the number is the saturation, and the card
    /// would land one box away from the one that is marked. Synchronous edges only, which is
    /// exactly what CallCost adds latency for: an asynchronous hand-off costs the caller nothing
    /// (D4.7). A cycle is cut by the visited set, as CallCost cuts it at the edge that closes it.
    /// </summary>
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
