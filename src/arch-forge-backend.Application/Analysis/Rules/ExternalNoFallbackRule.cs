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
/// </summary>
public class ExternalNoFallbackRule : IArchitectureRule
{
    public string Code => RuleCodes.ExternalNoFallback;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Critical;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.EXTERNAL_NO_FALLBACK.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.EXTERNAL_NO_FALLBACK.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var calls = graph.Connections
            .Where(c => c.Synchronous)
            .Where(c => graph.NodesById.TryGetValue(c.TargetNodeId, out var target) && target.Type == NodeType.ExternalService)
            .ToList();

        if (calls.Count == 0)
            return RuleResult.NotApplicable;

        var findings = calls
            .Where(c => graph.ResilienceOf(c) is { CircuitBreaker: false, Fallback: false })
            .OrderBy(c => c.Id)
            .Select(edge => new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                LocalizedText.Of("finding.EXTERNAL_NO_FALLBACK.unprotected.message",
                    ("source", graph.NodesById[edge.SourceNodeId].Name),
                    ("target", graph.NodesById[edge.TargetNodeId].Name)),
                LocalizedText.Of("finding.EXTERNAL_NO_FALLBACK.unprotected.explanation",
                    ("target", graph.NodesById[edge.TargetNodeId].Name)),
                LocalizedText.Of("finding.EXTERNAL_NO_FALLBACK.unprotected.suggestion"),
                [FindingElement.Connection(edge.Id)])
            {
                // D5.6: the fallback is the half with a single answer. Which breaker policy, and
                // what the alternative response actually is, are decisions - turning them into a
                // button would be the analyzer deciding the product for you.
                Fix = new AppliedFix(
                    FixKinds.SetEdgeResilience,
                    FindingElement.Connection(edge.Id),
                    LocalizedText.Of("fix.EXTERNAL_NO_FALLBACK.addFallback"),
                    Fallback: true)
            })
            .ToList();

        return RuleResult.Applicable(findings);
    }
}
