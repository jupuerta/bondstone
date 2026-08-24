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

    [Fact]
    public void AThirdPartysOwnCall_IsNotYoursToProtect()
    {
        // V14 B10. `Stripe -> Maps` bare: what Stripe does with its call to Google Maps is
        // Stripe's resilience, and there is no knob on this canvas that changes it.
        _builder
            .Node("Api", NodeType.Api)
            .Node("Stripe", NodeType.ExternalService)
            .Node("Maps", NodeType.ExternalService)
            .Connect("Api", "Stripe", timeoutSeconds: 2, fallback: true)
            .Connect("Stripe", "Maps");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    // ---- V15 A3: an instrumentation edge has two directions, and only one of them is a call ----

    [Fact]
    public void AScrape_IsNotACallOfYours()
    {
        // V15 A3 (QA V15 C2). `Obs -> Api` is the telemetry reading what it watches: it starts at
        // the metrics sink, it is on no request path, and there is no knob on it the architect
        // owns - a timeout on Prometheus's poll of your Api is Prometheus's, and a fallback on it
        // means nothing. The canvas draws every new arrow synchronous and unconfigured, so wiring
        // the telemetry the way NO_OBSERVABILITY asks used to cost a Warning per box.
        _builder
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Db", timeoutSeconds: 5)
            .Connect("Obs", "Api")
            .Connect("Obs", "Db");

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void AScrapeAsTheOnlySynchronousCall_LeavesNothingToEvaluate()
    {
        // The same courtesy `IsNotYourCall` gets: a graph whose only synchronous arrows are the
        // telemetry's has no call this rule can grade, so it is not applicable rather than passed.
        _builder
            .Node("Api", NodeType.Api)
            .Node("Obs", NodeType.Observability)
            .Connect("Obs", "Api");

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }

    [Fact]
    public void AScrapeTowardsACriticalSpof_IsNotTheCriticalItUsedToBe()
    {
        // The worst of it: `Obs -> Gw` with the gateway a Critical SPOF came out a Critical whose
        // explanation said the gateway's fall "propagates straight back" - into the metrics
        // collector. The escalation read the SPOF fact without asking which edge it was on.
        _builder
            .Node("Gw", NodeType.Gateway)
            .Node("Svc")
            .Node("Obs", NodeType.Observability)
            .Connect("Gw", "Svc", timeoutSeconds: 5, fallback: true)
            .Connect("Obs", "Gw");

        var graph = _builder.Build();
        graph.Facts.Add(new SinglePointOfFailureFact(_builder.IdOf("Gw"), Severity.Critical));

        Assert.Empty(_rule.Evaluate(graph).Findings);
    }

    [Fact]
    public void ASynchronousPushToTheTelemetry_IsStillAWarning_AndItsFixMakesTheHopAsynchronous()
    {
        // The half of the exception that was always justified: `Api -> Obs` synchronous is a
        // blocking call to the metrics backend on the request path, the one thing about an
        // instrumentation edge that is genuinely the architect's. What the student can do about it
        // is what the doctrine names - make the hop asynchronous - not a timeout and a fallback on
        // a fire-and-forget send, which is what the button used to draw.
        _builder
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Db", timeoutSeconds: 5)
            .Connect("Api", "Obs");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.MISSING_RESILIENCE.unprotected.message(source=Api, target=Obs)", finding.Message.Describe());
        Assert.Equal("finding.MISSING_RESILIENCE.telemetryPush.explanation(target=Obs)", finding.Explanation.Describe());
        Assert.Equal("finding.MISSING_RESILIENCE.telemetryPush.suggestion", finding.Suggestion.Describe());
        Assert.Equal(_builder.EdgeElement("Api", "Obs"), Assert.Single(finding.Elements));

        var fix = finding.Fix;
        Assert.NotNull(fix);
        Assert.Equal(FixKinds.MakeEdgeAsynchronous, fix!.Kind);
        Assert.Equal(_builder.EdgeElement("Api", "Obs"), fix.Element);
        Assert.Equal("fix.MISSING_RESILIENCE.makeAsynchronous", fix.Label.Describe());
        Assert.Null(fix.TimeoutSeconds);   // the mode is the fix; nothing else on the edge moves
        Assert.Null(fix.Fallback);
    }

    [Fact]
    public void APushToTheTelemetry_NeverEscalates()
    {
        // SPOF never grades the telemetry box, so in the real analyzer this fact does not exist.
        // The rule refuses the escalation on its own anyway: a component's fall does not propagate
        // back into its own metrics, whatever a fact says about the box.
        _builder
            .Node("Api", NodeType.Api)
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Obs");

        var graph = _builder.Build();
        graph.Facts.Add(new SinglePointOfFailureFact(_builder.IdOf("Obs"), Severity.Critical));

        var finding = Assert.Single(_rule.Evaluate(graph).Findings);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.MISSING_RESILIENCE.telemetryPush.explanation", finding.Explanation.Key());
    }

    [Fact]
    public void APushWithATimeout_Passes()
    {
        // Bounded is bounded: a synchronous send that cannot hang the request is the same
        // protected call any other edge with a timeout is.
        _builder
            .Node("Api", NodeType.Api)
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Obs", timeoutSeconds: 2);

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void TheTwoDirectionsOfAnInstrumentationEdge_AreNotTheSameFinding()
    {
        // QA V15 S1a / S1b: the two used to come out letter for letter identical, with a timeout
        // and a fallback offered on a scrape. The rule now reads the arrow.
        var push = new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Obs", NodeType.Observability)
            .Connect("Api", "Obs");
        var scrape = new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Obs", NodeType.Observability)
            .Connect("Obs", "Api");

        Assert.Single(_rule.Evaluate(push.Build()).Findings);
        Assert.Empty(_rule.Evaluate(scrape.Build()).Findings);
    }
}
