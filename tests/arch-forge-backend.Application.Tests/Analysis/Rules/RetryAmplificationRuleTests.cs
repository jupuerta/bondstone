using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

public class RetryAmplificationRuleTests
{
    private readonly RetryAmplificationRule _rule = new();
    private readonly GraphBuilder _builder = new();

    [Fact]
    public void RetriesWithoutTimeout_IsAWarning()
    {
        _builder
            .Nodes(default, "Orders", "Payments")
            .Connect("Orders", "Payments", retries: 3);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(RuleCodes.RetryAmplification, finding.Code);
        Assert.Equal(Area.Resilience, finding.Area);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(["Orders", "Orders->Payments", "Payments"], _builder.Names(finding.Elements));
    }

    [Fact]
    public void RetriesWithATimeout_DoesNotWarnOnItsOwn()
    {
        // A bounded retry is not the problem; the problem is retrying with nothing to stop it.
        _builder
            .Nodes(default, "Orders", "Payments")
            .Connect("Orders", "Payments", timeoutSeconds: 5, retries: 3);

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void FactorReachingTenAlongAChain_IsCritical()
    {
        // The worked example of D4.x: 3 x 4 = 12 requests for every original one.
        _builder
            .Nodes(default, "Gateway", "Orders", "Shipping")
            .Connect("Gateway", "Orders", timeoutSeconds: 5, retries: 2)
            .Connect("Orders", "Shipping", timeoutSeconds: 5, retries: 3);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal(
            "Los reintentos se multiplican a lo largo de la cadena Gateway → Orders → Shipping: hasta 12 peticiones por cada una original.",
            finding.Message);
        Assert.Equal(
            ["Gateway", "Gateway->Orders", "Orders", "Orders->Shipping", "Shipping"],
            _builder.Names(finding.Elements));
    }

    [Fact]
    public void FactorBelowTheThreshold_DoesNotEscalate()
    {
        // 3 x 3 = 9: bad, but not yet the avalanche the threshold marks.
        _builder
            .Nodes(default, "Gateway", "Orders", "Shipping")
            .Connect("Gateway", "Orders", timeoutSeconds: 5, retries: 2)
            .Connect("Orders", "Shipping", timeoutSeconds: 5, retries: 2);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void AsynchronousHopBreaksTheAmplification()
    {
        // A queue absorbs the retries instead of passing them on.
        _builder
            .Nodes(default, "Gateway", "Orders", "Shipping")
            .Connect("Gateway", "Orders", timeoutSeconds: 5, retries: 2)
            .Connect("Orders", "Shipping", synchronous: false, timeoutSeconds: 5, retries: 3);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void AmplifiedChain_NamesOnlyTheHopsThatRetry()
    {
        // Hops that retry nothing multiply by one; naming them would point the user at components
        // that are not part of the problem.
        _builder
            .Nodes(default, "Edge", "Gateway", "Orders", "Shipping", "Db")
            .Connect("Edge", "Gateway", timeoutSeconds: 5)
            .Connect("Gateway", "Orders", timeoutSeconds: 5, retries: 2)
            .Connect("Orders", "Shipping", timeoutSeconds: 5, retries: 3)
            .Connect("Shipping", "Db", timeoutSeconds: 5);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Contains("cadena Gateway → Orders → Shipping:", finding.Message);
        Assert.Contains("hasta 12 peticiones", finding.Message);
    }

    [Fact]
    public void AmplifiedChain_PublishesAFactPerEdgeSoMissingResilienceStaysQuiet()
    {
        // D4.5: the same edge must not be told twice, under two codes, that it lacks a timeout.
        _builder
            .Nodes(default, "Gateway", "Orders", "Shipping")
            .Connect("Gateway", "Orders", timeoutSeconds: 5, retries: 2)
            .Connect("Orders", "Shipping", timeoutSeconds: 5, retries: 3);

        var graph = _builder.Build();
        _rule.Evaluate(graph);

        Assert.Equal(
            [_builder.EdgeId("Gateway", "Orders"), _builder.EdgeId("Orders", "Shipping")],
            graph.Facts.OfType<RetryAmplificationFact>().Select(f => f.ConnectionId).Order());
    }

    [Fact]
    public void EdgeAlreadyInAnAmplifiedChain_IsNotAlsoReportedAsAWarning()
    {
        // One finding per edge, the more specific one.
        _builder
            .Nodes(default, "Gateway", "Orders", "Shipping")
            .Connect("Gateway", "Orders", retries: 4)
            .Connect("Orders", "Shipping", timeoutSeconds: 5, retries: 1);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
    }

    [Fact]
    public void NoRetriesConfigured_IsNotApplicable()
    {
        _builder
            .Nodes(default, "Api", "Db")
            .Connect("Api", "Db", timeoutSeconds: 5);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }

    [Fact]
    public void CycleInTheSynchronousGraph_DoesNotHangTheWalk()
    {
        // CYCLE runs in the same phase, so its facts are not available here (D5.2) - the walk has
        // to guard against cycles on its own.
        _builder
            .Nodes(default, "A", "B", "C")
            .Connect("A", "B", retries: 1, timeoutSeconds: 5)
            .Connect("B", "C", retries: 1, timeoutSeconds: 5)
            .Connect("C", "A", retries: 1, timeoutSeconds: 5);

        Assert.True(_rule.Evaluate(_builder.Build()).IsApplicable);
    }
}
