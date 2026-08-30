using arch_forge_backend.Application.Localization;
using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Analysis.Rules;

public class MissingResilienceRule : IArchitectureRule
{
    private const int FixTimeoutSeconds = 5;

    public string Code => RuleCodes.MissingResilience;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.MISSING_RESILIENCE.passed.message");

    public LocalizedText PassedExplanation => LocalizedText.Of("finding.MISSING_RESILIENCE.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var synchronous = graph.Connections
            .Where(c => c.Synchronous
                        && !NodeRoles.IsNotYourCall(graph, c)
                        && !NodeRoles.IsTelemetryScrape(graph, c))
            .ToList();

        if (synchronous.Count == 0)
            return RuleResult.NotApplicable("rule.na.MISSING_RESILIENCE.noSyncCall");

        var criticalSpofs = graph.Facts
            .OfType<SinglePointOfFailureFact>()
            .Where(fact => fact.Severity == Severity.Critical)
            .Select(fact => fact.NodeId)
            .ToHashSet();

        var alreadyReported = graph.Facts.OfType<RetryAmplificationFact>().Select(fact => fact.ConnectionId)
            .Concat(graph.Facts.OfType<ExternalNoFallbackFact>().Select(fact => fact.ConnectionId))
            .ToHashSet();

        var findings = new List<RuleFinding>();

        foreach (var edge in synchronous.Where(c => !alreadyReported.Contains(c.Id)))
        {
            var resilience = graph.ResilienceOf(edge);
            var hasTimeout = resilience.TimeoutSeconds is not null;

            var push = NodeRoles.IsTelemetryPush(graph, edge);

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
                Fix = push ? MakeAsynchronous(edge) : Protect(graph, edge, hasTimeout)
            });
        }

        return RuleResult.Applicable(findings);
    }

    // A router has nothing to answer with, so the button never ticks a fallback on its call — the
    // drawing would gain an IMPLAUSIBLE_DEPENDENCY for it (QA 30-08, 7.4). A breaker it can have.
    private static AppliedFix Protect(ArchitectureGraph graph, Connection edge, bool hasTimeout)
    {
        if (NodeRoles.ForwardsRequest(graph, edge))
            return new AppliedFix(
                FixKinds.SetEdgeResilience,
                FindingElement.Connection(edge.Id),
                hasTimeout
                    ? LocalizedText.Of("fix.MISSING_RESILIENCE.addCircuitBreaker")
                    : LocalizedText.Of("fix.MISSING_RESILIENCE.timeoutAndCircuitBreaker", ("seconds", FixTimeoutSeconds)),
                TimeoutSeconds: hasTimeout ? null : FixTimeoutSeconds,
                CircuitBreaker: true);

        return new AppliedFix(
            FixKinds.SetEdgeResilience,
            FindingElement.Connection(edge.Id),
            hasTimeout
                ? LocalizedText.Of("fix.MISSING_RESILIENCE.addFallback")
                : LocalizedText.Of("fix.MISSING_RESILIENCE.timeoutAndFallback", ("seconds", FixTimeoutSeconds)),
            TimeoutSeconds: hasTimeout ? null : FixTimeoutSeconds,
            Fallback: true);
    }

    private static AppliedFix MakeAsynchronous(Connection edge) => new(
        FixKinds.MakeEdgeAsynchronous,
        FindingElement.Connection(edge.Id),
        LocalizedText.Of("fix.MISSING_RESILIENCE.makeAsynchronous"));

    private static string Variant(bool hasTimeout, bool exposedToSpof) => (hasTimeout, exposedToSpof) switch
    {
        (false, true) => "noTimeoutAndSpof",
        (true, _) => "exposedToSpof",
        _ => "noTimeout"
    };
}
