using arch_forge_backend.Application.Localization;
using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V2 D4.4. Only synchronous edges - on an asynchronous one these mechanisms do not apply.
///
/// This is the rule that put <c>Facts</c> into service: it needs to know whether another rule
/// marked the destination as a single point of failure, and whether RETRY_AMPLIFICATION or
/// EXTERNAL_NO_FALLBACK already spoke about the edge (D4.5, QA B8).
///
/// <strong>V15 A3. And it is the one rule that still reads an instrumentation edge - half of
/// one.</strong> The doctrine of <see cref="NodeRoles.IsTelemetryEdge"/> keeps this rule as its
/// deliberate exception because a blocking call to the metrics backend on the request path is a
/// real finding. That is <c>Api → Obs</c>, a push. The rule used to fire identically on
/// <c>Obs → Api</c> - a scrape, on no request path, with nothing on it the architect configures -
/// and since the canvas draws every new arrow synchronous and unconfigured, wiring the telemetry
/// the way NO_OBSERVABILITY asks cost a Warning per box and, towards the front door, a Critical
/// whose explanation said the Api's fall "propagates straight back": into the metrics collector.
/// A scrape is now skipped like every other instrumentation edge; a push is graded on its timeout,
/// never escalated, and fixed the way the doctrine names - by making the hop asynchronous.
/// </summary>
public class MissingResilienceRule : IArchitectureRule
{
    /// <summary>
    /// What the one-click fix sets, and the number the button says out loud. A constant because the
    /// label and the transformation have to agree - two literals drift the first time one is tuned.
    /// </summary>
    private const int FixTimeoutSeconds = 5;

    public string Code => RuleCodes.MissingResilience;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.MISSING_RESILIENCE.passed.message");

    public LocalizedText PassedExplanation => LocalizedText.Of("finding.MISSING_RESILIENCE.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        // The outside's edges are the requests themselves, not calls anybody configures (QA
        // §3.1): drawing a Client must not open every diagram with "your users' call has no
        // timeout". A third party's own calls are not yours to configure either (V14 B10). And
        // the telemetry's own reads are not calls at all (V15 A3): a scrape starts at the metrics
        // sink, sits on no request path, and a timeout or a fallback on it would be nobody's.
        var synchronous = graph.Connections
            .Where(c => c.Synchronous
                        && !NodeRoles.IsNotYourCall(graph, c)
                        && !NodeRoles.IsTelemetryScrape(graph, c))
            .ToList();

        if (synchronous.Count == 0)
            return RuleResult.NotApplicable;

        // Escalation asks for a Critical SPOF, not just any SPOF: with severity now graded by
        // blast radius, requiring only "is a SPOF" flagged every call to any component shared by
        // two consumers, and a correct architecture with one shared cache came out full of
        // criticals (D4.4, correction v1.1).
        var criticalSpofs = graph.Facts
            .OfType<SinglePointOfFailureFact>()
            .Where(fact => fact.Severity == Severity.Critical)
            .Select(fact => fact.NodeId)
            .ToHashSet();

        // Saying "you are missing a timeout" twice, under two different codes, is what D4.5 calls
        // noise. The more specific rule already said it - RETRY_AMPLIFICATION on a call that
        // retries, EXTERNAL_NO_FALLBACK on a call to somebody else's service (QA B8: its Critical
        // already names the timeout when there is none and its fix sets it, so this Warning on
        // the same edge was the same scolding twice in the same area).
        var alreadyReported = graph.Facts.OfType<RetryAmplificationFact>().Select(fact => fact.ConnectionId)
            .Concat(graph.Facts.OfType<ExternalNoFallbackFact>().Select(fact => fact.ConnectionId))
            .ToHashSet();

        var findings = new List<RuleFinding>();

        foreach (var edge in synchronous.Where(c => !alreadyReported.Contains(c.Id)))
        {
            var resilience = graph.ResilienceOf(edge);
            var hasTimeout = resilience.TimeoutSeconds is not null;

            // V15 A3. A component sending its telemetry synchronously is the one instrumentation
            // edge left in the list, and it is graded on its timeout alone: the escalation below
            // is about the destination taking the system down with it, and nothing falls with its
            // metrics sink - SPOF never grades the telemetry box, and a fact about it would still
            // not turn a send into a Critical.
            var push = NodeRoles.IsTelemetryPush(graph, edge);

            // Asking for a circuit breaker on every synchronous edge would flag practically the
            // whole graph. Asking for it only where the destination already takes the system down
            // puts the message where the risk is (D4.4).
            var exposedToSpof = !push
                                && !resilience.CircuitBreaker
                                && !resilience.Fallback
                                && criticalSpofs.Contains(edge.TargetNodeId);

            if (hasTimeout && !exposedToSpof)
                continue;

            var severity = exposedToSpof ? Severity.Critical : Severity.Warning;
            var source = graph.NodesById[edge.SourceNodeId].Name;
            var target = graph.NodesById[edge.TargetNodeId].Name;
            var variant = push ? "telemetryPush" : Variant(hasTimeout, exposedToSpof);

            findings.Add(new RuleFinding(
                Code,
                Area,
                severity,
                LocalizedText.Of("finding.MISSING_RESILIENCE.unprotected.message",
                    ("source", source),
                    ("target", target)),
                LocalizedText.Of($"finding.MISSING_RESILIENCE.{variant}.explanation", ("target", target)),
                LocalizedText.Of($"finding.MISSING_RESILIENCE.{variant}.suggestion"),
                [FindingElement.Connection(edge.Id)])
            {
                Fix = push ? MakeAsynchronous(edge) : Protect(edge, hasTimeout)
            });
        }

        return RuleResult.Applicable(findings);
    }

    /// <summary>
    /// V4 D5.6. There is nothing to decide here: without a timeout no mechanism of the edge can
    /// act at all (D4.2), and the alternative is what stops the fall. The timeout is set well
    /// above what the call costs so the fix does not create a TIMEOUT_TOO_SHORT of its own.
    /// </summary>
    private static AppliedFix Protect(Connection edge, bool hasTimeout) => new(
        FixKinds.SetEdgeResilience,
        FindingElement.Connection(edge.Id),
        hasTimeout
            ? LocalizedText.Of("fix.MISSING_RESILIENCE.addFallback")
            : LocalizedText.Of("fix.MISSING_RESILIENCE.timeoutAndFallback", ("seconds", FixTimeoutSeconds)),
        TimeoutSeconds: hasTimeout ? null : FixTimeoutSeconds,
        Fallback: true);

    /// <summary>
    /// V15 A3. Telemetry is fire-and-forget, so the fix for a send that waits is to stop waiting:
    /// the arrow's mode, not a timeout and a fallback on it. It is what NO_OBSERVABILITY's own fix
    /// draws, and the one thing the doctrine says the student can do about this edge. Nothing
    /// else on the edge moves.
    /// </summary>
    private static AppliedFix MakeAsynchronous(Connection edge) => new(
        FixKinds.MakeEdgeAsynchronous,
        FindingElement.Connection(edge.Id),
        LocalizedText.Of("fix.MISSING_RESILIENCE.makeAsynchronous"));

    /// <summary>
    /// Which of the three situations this edge is in. The three texts of §6.1 say what happens, why
    /// it matters and what to do, so the "why" has to name the reason that actually applies here,
    /// not the union of both. (The fourth, a send to the telemetry, is chosen before this is asked:
    /// it is never a SPOF case, and its sentence is its own - V15 A3.)
    ///
    /// <strong>Three named cases and not two flags joined with a space.</strong> Until V7 the
    /// explanation was <c>string.Join(" ", reasons)</c> over two fragments, the second of which
    /// opened with <em>"Y como…"</em> - a connector that in English does not go in that position,
    /// and in some languages does not exist as a separate word at all. Composing a sentence out of
    /// pieces reads fine in the language it was written in and comes apart in the next one, so the
    /// combination is chosen here and the whole sentence lives in the catalogue (D1.1).
    /// </summary>
    private static string Variant(bool hasTimeout, bool exposedToSpof) => (hasTimeout, exposedToSpof) switch
    {
        (false, true) => "noTimeoutAndSpof",
        (true, _) => "exposedToSpof",
        _ => "noTimeout"
    };
}
