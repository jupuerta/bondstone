using arch_forge_backend.Application.Localization;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V8 §6.2, and the rule that earns <see cref="NodeType.ExternalService"/> its place under V2 D3.2:
/// a type nothing reads would be a pretty box.
///
/// <strong>A synchronous call to somebody else's service with nothing to fall back on.</strong> It
/// is the same shape as <see cref="MissingResilienceRule"/> and it is not the same finding: there,
/// a missing timeout or fallback is a call that could be protected and is not, and the way out
/// includes making the target sturdier. Here <em>there is no other way out</em> - you cannot scale
/// the provider, you cannot deploy a fix to it, and you cannot page whoever can. Containing the
/// call is the whole of what you are allowed to do, so not containing it is worse than it is
/// anywhere else, and that is why this one is Critical while its sibling grades.
///
/// A timeout on its own does not clear it. A timeout bounds the wait, which is what makes every
/// other mechanism possible (V3 D4.2), but the caller still ends up with no answer - and what the
/// user is asked for here is <em>an answer without the provider</em>: a fallback, or a breaker that
/// stops asking. An asynchronous call is not reported: nobody is waiting, so there is nothing to
/// contain.
///
/// <strong>QA B8: one call, one finding.</strong> Until scoring v6 an unconfigured external call
/// paid twice in the same area - this Critical and MISSING_RESILIENCE's Warning about the missing
/// timeout, on the same edge. That is the pattern D4.5 calls noise: two codes scolding you for not
/// protecting one call. So this rule is now a Producer, like RETRY_AMPLIFICATION: it publishes an
/// <see cref="ExternalNoFallbackFact"/> for every edge it reports, MISSING_RESILIENCE stays quiet
/// on those, and the timeout is absorbed here - the edge without one gets its own variant, whose
/// text says both halves and whose fix sets both, so clearing this finding never leaves the
/// Warning it used to hide standing behind it.
/// </summary>
public class ExternalNoFallbackRule : IArchitectureRule
{
    /// <summary>
    /// The same number MISSING_RESILIENCE's fix sets, for the same reason: well above what any
    /// call in the latency model costs, so the fix does not create a TIMEOUT_TOO_SHORT of its own.
    /// </summary>
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
            // A Client calling a third party directly is their conversation, not a call this
            // architecture can contain (QA §3.1) - and neither is a third party calling another
            // (V14 B10): `Stripe -> Maps` is Stripe's timeout to set, not yours.
            .Where(c => !NodeRoles.IsNotYourCall(graph, c))
            .Where(c => graph.NodesById.TryGetValue(c.TargetNodeId, out var target) && target.Type == NodeType.ExternalService)
            .ToList();

        if (calls.Count == 0)
            return RuleResult.NotApplicable;

        var findings = new List<RuleFinding>();

        foreach (var edge in calls.Where(c => graph.ResilienceOf(c) is { CircuitBreaker: false, Fallback: false }).OrderBy(c => c.Id))
        {
            // D4.5: whatever MISSING_RESILIENCE would have said about this edge is said here.
            graph.Facts.Add(new ExternalNoFallbackFact(edge.Id));

            var hasTimeout = graph.ResilienceOf(edge).TimeoutSeconds is not null;
            var source = graph.NodesById[edge.SourceNodeId].Name;
            var target = graph.NodesById[edge.TargetNodeId].Name;

            // Two whole sentences and not a shared one plus a tail about the timeout: the join
            // reads fine in the language it was written in and falls apart in the next (V7 D1.1).
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
                // D5.6: the fallback is the half with a single answer. Which breaker policy, and
                // what the alternative response actually is, are decisions - turning them into a
                // button would be the analyzer deciding the product for you. The timeout travels
                // with it when it is missing, because a fallback without one contains nothing
                // (D4.2) and a fix that leaves the call half-protected is not a fix.
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
