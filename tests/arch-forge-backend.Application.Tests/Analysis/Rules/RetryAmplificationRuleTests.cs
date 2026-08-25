using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
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
        _builder
            .Nodes(default, "Gateway", "Orders", "Shipping")
            .Connect("Gateway", "Orders", timeoutSeconds: 5, retries: 2)
            .Connect("Orders", "Shipping", timeoutSeconds: 5, retries: 3);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.RETRY_AMPLIFICATION.chain.message(factor=12, walk=Gateway → Orders → Shipping)",
            finding.Message.Describe());
        Assert.Equal(
            ["Gateway", "Gateway->Orders", "Orders", "Orders->Shipping", "Shipping"],
            _builder.Names(finding.Elements));
    }

    [Fact]
    public void FactorBelowTheThreshold_DoesNotEscalate()
    {
        _builder
            .Nodes(default, "Gateway", "Orders", "Shipping")
            .Connect("Gateway", "Orders", timeoutSeconds: 5, retries: 2)
            .Connect("Orders", "Shipping", timeoutSeconds: 5, retries: 2);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void AsynchronousHopBreaksTheAmplification()
    {
        _builder
            .Nodes(default, "Gateway", "Orders", "Shipping")
            .Connect("Gateway", "Orders", timeoutSeconds: 5, retries: 2)
            .Connect("Orders", "Shipping", synchronous: false, timeoutSeconds: 5, retries: 3);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void AmplifiedChain_NamesOnlyTheHopsThatRetry()
    {
        _builder
            .Nodes(default, "Edge", "Gateway", "Orders", "Shipping", "Db")
            .Connect("Edge", "Gateway", timeoutSeconds: 5)
            .Connect("Gateway", "Orders", timeoutSeconds: 5, retries: 2)
            .Connect("Orders", "Shipping", timeoutSeconds: 5, retries: 3)
            .Connect("Shipping", "Db", timeoutSeconds: 5);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.RETRY_AMPLIFICATION.chain.message(factor=12, walk=Gateway → Orders → Shipping)",
            finding.Message.Describe());
    }

    [Fact]
    public void AmplifiedChain_PublishesAFactPerEdgeSoMissingResilienceStaysQuiet()
    {
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
        _builder
            .Nodes(default, "A", "B", "C")
            .Connect("A", "B", retries: 1, timeoutSeconds: 5)
            .Connect("B", "C", retries: 1, timeoutSeconds: 5)
            .Connect("C", "A", retries: 1, timeoutSeconds: 5);

        Assert.True(_rule.Evaluate(_builder.Build()).IsApplicable);
    }

    [Fact]
    public void AThirdPartysRetries_AreNotPartOfYourStorm()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Stripe", NodeType.ExternalService)
            .Node("Maps", NodeType.ExternalService)
            .Connect("Api", "Stripe", timeoutSeconds: 2, fallback: true)
            .Connect("Stripe", "Maps", retries: 5, timeoutSeconds: 5);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }
}
