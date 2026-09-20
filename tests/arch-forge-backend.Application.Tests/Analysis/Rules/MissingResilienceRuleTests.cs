using bondstone_backend.Application.Tests.TestDoubles;
using bondstone_backend.Application.Localization;
using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Analysis.Rules;
using bondstone_backend.Application.Tests.Analysis.Rules.Support;
using bondstone_backend.Domain.Enums;
using Xunit;

namespace bondstone_backend.Application.Tests.Analysis.Rules;

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
        Assert.Equal("finding.MISSING_RESILIENCE.exposedToSpof.explanation(target=Db)", finding.Explanation.Describe());
    }

    [Fact]
    public void TimeoutTowardsAWarningSpof_DoesNotEscalate()
    {
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
        Assert.Equal("finding.MISSING_RESILIENCE.noTimeoutAndSpof.explanation", finding.Explanation.Key());
        Assert.Equal("Db", finding.Explanation.Arg("target"));
        Assert.Equal("finding.MISSING_RESILIENCE.noTimeoutAndSpof.suggestion", finding.Suggestion.Key());
    }

    [Fact]
    public void EdgeAlreadyReportedByRetryAmplification_StaysQuiet()
    {
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
        _builder
            .Node("Api", NodeType.Api)
            .Node("Stripe", NodeType.ExternalService)
            .Node("Maps", NodeType.ExternalService)
            .Connect("Api", "Stripe", timeoutSeconds: 2, fallback: true)
            .Connect("Stripe", "Maps");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }


    [Fact]
    public void AScrape_IsNotACallOfYours()
    {
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
        _builder
            .Node("Api", NodeType.Api)
            .Node("Obs", NodeType.Observability)
            .Connect("Obs", "Api");

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }

    [Fact]
    public void AScrapeTowardsACriticalSpof_IsNotTheCriticalItUsedToBe()
    {
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
        Assert.Null(fix.TimeoutSeconds);
        Assert.Null(fix.Fallback);
    }

    [Fact]
    public void APushToTheTelemetry_NeverEscalates()
    {
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
