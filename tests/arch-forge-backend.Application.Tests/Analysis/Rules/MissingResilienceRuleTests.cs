using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

public class MissingResilienceRuleTests
{
    private readonly MissingResilienceRule _rule = new();
    private readonly GraphBuilder _builder = new();

    [Fact]
    public void SynchronousEdgeWithoutTimeout_IsAWarning()
    {
        _builder
            .Node("Orders")
            .Node("Db", NodeType.Database)
            .Connect("Orders", "Db");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(RuleCodes.MissingResilience, finding.Code);
        Assert.Equal(Area.Resilience, finding.Area);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.MISSING_RESILIENCE.unprotected.message(source=Orders, target=Db)", finding.Message.Describe());
        Assert.Equal(_builder.EdgeElement("Orders", "Db"), Assert.Single(finding.Elements));
    }

    [Fact]
    public void AsynchronousEdge_IsIgnored()
    {
        // On an asynchronous call these mechanisms do not apply.
        _builder
            .Node("Orders")
            .Node("Queue", NodeType.Queue)
            .Connect("Orders", "Queue", synchronous: false)
            .Node("Api", NodeType.Api).Connect("Api", "Orders", timeoutSeconds: 5);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void TimeoutTowardsANodeThatIsNotASpof_Passes()
    {
        // Asking for a circuit breaker everywhere would flag the whole graph (D4.4).
        _builder
            .Node("Orders")
            .Node("Db", NodeType.Database)
            .Connect("Orders", "Db", timeoutSeconds: 5);

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void TimeoutTowardsACriticalSpofWithoutFallback_EscalatesToCritical()
    {
        _builder
            .Node("Orders")
            .Node("Db", NodeType.Database)
            .Connect("Orders", "Db", timeoutSeconds: 5);

        var graph = _builder.Build();
        graph.Facts.Add(new SinglePointOfFailureFact(_builder.IdOf("Db"), Severity.Critical));

        var finding = Assert.Single(_rule.Evaluate(graph).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        // Only the SPOF half applies: this edge has a timeout, so there is nothing to say about one.
        Assert.Equal("finding.MISSING_RESILIENCE.exposedToSpof.explanation(target=Db)", finding.Explanation.Describe());
    }

    [Fact]
    public void TimeoutTowardsAWarningSpof_DoesNotEscalate()
    {
        // The correction of v1.1: with SPOF graded by blast radius, escalating towards any SPOF
        // turned a correct architecture with one shared cache into a pile of criticals.
        _builder
            .Node("Orders")
            .Node("Cache", NodeType.Cache)
            .Connect("Orders", "Cache", timeoutSeconds: 5);

        var graph = _builder.Build();
        graph.Facts.Add(new SinglePointOfFailureFact(_builder.IdOf("Cache"), Severity.Warning));

        Assert.Empty(_rule.Evaluate(graph).Findings);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CircuitBreakerOrFallbackTowardsACriticalSpof_Passes(bool circuitBreaker, bool fallback)
    {
        _builder
            .Node("Orders")
            .Node("Db", NodeType.Database)
            .Connect("Orders", "Db", timeoutSeconds: 5, circuitBreaker: circuitBreaker, fallback: fallback);

        var graph = _builder.Build();
        graph.Facts.Add(new SinglePointOfFailureFact(_builder.IdOf("Db"), Severity.Critical));

        Assert.Empty(_rule.Evaluate(graph).Findings);
    }

    [Fact]
    public void NoTimeoutTowardsACriticalSpof_IsCriticalAndSaysBothReasons()
    {
        _builder
            .Node("Orders")
            .Node("Db", NodeType.Database)
            .Connect("Orders", "Db");

        var graph = _builder.Build();
        graph.Facts.Add(new SinglePointOfFailureFact(_builder.IdOf("Db"), Severity.Critical));

        var finding = Assert.Single(_rule.Evaluate(graph).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        // Both reasons apply, and that is one key rather than two fragments joined (V7 D1.1).
        Assert.Equal("finding.MISSING_RESILIENCE.noTimeoutAndSpof.explanation", finding.Explanation.Key());
        Assert.Equal("Db", finding.Explanation.Arg("target"));
        Assert.Equal("finding.MISSING_RESILIENCE.noTimeoutAndSpof.suggestion", finding.Suggestion.Key());
    }

    [Fact]
    public void EdgeAlreadyReportedByRetryAmplification_StaysQuiet()
    {
        // D4.5: the more specific rule already said the timeout is missing. Saying it again under
        // a second code is noise, not information.
        _builder
            .Node("Orders")
            .Node("Payments")
            .Connect("Orders", "Payments", retries: 3);

        var graph = _builder.Build();
        graph.Facts.Add(new RetryAmplificationFact(_builder.EdgeId("Orders", "Payments")));

        Assert.Empty(_rule.Evaluate(graph).Findings);
    }

    [Fact]
    public void EdgeAlreadyReportedByExternalNoFallback_StaysQuiet()
    {
        // QA B8: the same D4.5 courtesy towards EXTERNAL_NO_FALLBACK. Its Critical on a call to a
        // third party already names the missing timeout and its fix sets it - a second Warning on
        // the same edge was the same advice charged twice in the same area.
        _builder
            .Node("Checkout")
            .Node("Payments", NodeType.ExternalService)
            .Connect("Checkout", "Payments");

        var graph = _builder.Build();
        graph.Facts.Add(new ExternalNoFallbackFact(_builder.EdgeId("Checkout", "Payments")));

        var result = _rule.Evaluate(graph);

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void ExternalCallThatIsContainedButUnbounded_IsStillMissingItsTimeout()
    {
        // The fact only travels with a finding: a call with a fallback and no timeout is not
        // EXTERNAL_NO_FALLBACK's to report, so the timeout lesson is still this rule's to give -
        // and it matters, because a fallback without a timeout contains nothing (D4.2).
        _builder
            .Node("Checkout")
            .Node("Payments", NodeType.ExternalService)
            .Connect("Checkout", "Payments", fallback: true);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.MISSING_RESILIENCE.noTimeout.explanation", finding.Explanation.Key());
    }

    [Fact]
    public void AtMostOneFindingPerEdge()
    {
        _builder
            .Node("Orders")
            .Node("Db", NodeType.Database)
            .Connect("Orders", "Db");

        var graph = _builder.Build();
        graph.Facts.Add(new SinglePointOfFailureFact(_builder.IdOf("Db"), Severity.Critical));

        Assert.Single(_rule.Evaluate(graph).Findings);
    }

    [Fact]
    public void NoSynchronousEdge_IsNotApplicable()
    {
        _builder
            .Node("Orders")
            .Node("Queue", NodeType.Queue)
            .Connect("Orders", "Queue", synchronous: false);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }
}
