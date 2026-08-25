using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

public class ImplausibleDependencyRuleTests
{
    private readonly ImplausibleDependencyRule _rule = new();
    private readonly GraphBuilder _builder = new();


    [Theory]
    [InlineData(NodeType.Cdn, NodeType.Database, "cdn", Severity.Critical)]
    [InlineData(NodeType.Database, NodeType.Client, "store", Severity.Warning)]
    [InlineData(NodeType.Scheduler, NodeType.Client, "client", Severity.Warning)]
    [InlineData(NodeType.Scheduler, NodeType.Cdn, "scheduler", Severity.Warning)]
    [InlineData(NodeType.ObjectStorage, NodeType.Cache, "store", Severity.Warning)]
    [InlineData(NodeType.Database, NodeType.Database, "store", Severity.Warning)]
    public void EveryRowOfTheTable_IsNoLongerSilence(NodeType source, NodeType target, string variant, Severity severity)
    {
        _builder.Node("A", source).Node("B", target).Connect("A", "B");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(RuleCodes.ImplausibleDependency, finding.Code);
        Assert.Equal(Area.Resilience, finding.Area);
        Assert.Equal(severity, finding.Severity);
        Assert.Equal($"finding.IMPLAUSIBLE_DEPENDENCY.{variant}.message", finding.Message.Key());
        Assert.Equal(_builder.EdgeElement("A", "B"), Assert.Single(finding.Elements));
    }

    [Fact]
    public void TheCdnThatRunsSql_IsTheOneCaseThatAsserts()
    {
        _builder.Node("Edge", NodeType.Cdn).Node("Db", NodeType.Database)
            .Connect("Edge", "Db", protocol: ProtocolType.Sql);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.IMPLAUSIBLE_DEPENDENCY.cdn.message(source=Edge, target=Db)", finding.Message.Describe());
    }


    [Theory]
    [InlineData(NodeType.Api)]
    [InlineData(NodeType.Gateway)]
    [InlineData(NodeType.LoadBalancer)]
    [InlineData(NodeType.Service)]
    [InlineData(NodeType.ObjectStorage)]
    [InlineData(NodeType.ExternalService)]
    public void ACdnInFrontOfAnOrigin_IsHowACdnIsDrawn(NodeType origin)
    {
        _builder.Node("Edge", NodeType.Cdn).Node("Origin", origin).Connect("Edge", "Origin");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Theory]
    [InlineData(NodeType.Worker)]
    [InlineData(NodeType.Service)]
    [InlineData(NodeType.Api)]
    [InlineData(NodeType.Queue)]
    [InlineData(NodeType.Topic)]
    [InlineData(NodeType.Gateway)]
    [InlineData(NodeType.ExternalService)]
    public void AClockFiringWork_IsWhatAClockIsFor(NodeType work)
    {
        _builder.Node("Cron", NodeType.Scheduler).Node("Work", work).Connect("Cron", "Work");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void TheTwoOrdinaryShapesOfAThirdParty_AreNoLongerFindings()
    {
        _builder
            .Node("Cron", NodeType.Scheduler).Node("Stripe", NodeType.ExternalService)
            .Node("Web", NodeType.Client).Node("Edge", NodeType.Cdn).Node("Origin", NodeType.ExternalService)
            .Connect("Cron", "Stripe")
            .Connect("Web", "Edge").Connect("Edge", "Origin");

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void WhatTheThirdPartyDoesNotBuy_IsTheStoreAndTheClientSentences()
    {
        _builder
            .Node("Db", NodeType.Database).Node("Stripe", NodeType.ExternalService)
            .Node("Cron", NodeType.Scheduler).Node("Web", NodeType.Client)
            .Connect("Db", "Stripe")
            .Connect("Cron", "Web");

        var findings = _rule.Evaluate(_builder.Build()).Findings;

        Assert.Equal(
            new[] { "finding.IMPLAUSIBLE_DEPENDENCY.client.message", "finding.IMPLAUSIBLE_DEPENDENCY.store.message" },
            findings.Select(f => f.Message.Key()).Order());
    }

    [Fact]
    public void ACacheReadingThroughToItsOrigin_IsNotAStoreOriginatingACall()
    {
        _builder.Node("Api", NodeType.Api).Node("Cache", NodeType.Cache).Node("Db", NodeType.Database)
            .Connect("Api", "Cache").Connect("Cache", "Db", protocol: ProtocolType.Sql);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void InstrumentationIsNotRead()
    {
        _builder.Node("Db", NodeType.Database).Node("Obs", NodeType.Observability)
            .Connect("Db", "Obs", synchronous: false).Connect("Obs", "Db", synchronous: false);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }

    [Fact]
    public void AThirdPartysOwnCalls_AreItsOwnDrawing()
    {
        _builder.Node("Stripe", NodeType.ExternalService).Node("Web", NodeType.Client)
            .Connect("Stripe", "Web");

        var result = _rule.Evaluate(_builder.Build());

        Assert.False(result.IsApplicable);
    }

    [Fact]
    public void ASelfLoop_IsCyclesFinding()
    {
        _builder.Node("Db", NodeType.Database).Connect("Db", "Db");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void TheOrdinaryLayeredWeb_IsClean()
    {
        _builder
            .Node("Web", NodeType.Client).Node("Edge", NodeType.Cdn).Node("LB", NodeType.LoadBalancer)
            .Node("Gw", NodeType.Gateway).Node("Api", NodeType.Api).Node("Svc", NodeType.Service)
            .Node("Cache", NodeType.Cache).Node("Db", NodeType.Database).Node("Queue", NodeType.Queue)
            .Node("Worker", NodeType.Worker).Node("Cron", NodeType.Scheduler).Node("Files", NodeType.ObjectStorage)
            .Node("Auth", NodeType.IdentityProvider).Node("Stripe", NodeType.ExternalService)
            .Connect("Web", "Edge").Connect("Edge", "LB").Connect("LB", "Gw").Connect("Gw", "Api")
            .Connect("Gw", "Auth").Connect("Api", "Svc").Connect("Svc", "Cache").Connect("Cache", "Db", protocol: ProtocolType.Sql)
            .Connect("Api", "Queue", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Queue", "Worker", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Worker", "Db", protocol: ProtocolType.Sql).Connect("Worker", "Files").Connect("Cron", "Worker")
            .Connect("Svc", "Stripe");

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void TheWalkGoesIntoTheExplanationByName()
    {
        _builder.Node("Db", NodeType.Database).Node("Api", NodeType.Api).Connect("Db", "Api");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("Db", finding.Explanation.Arg("source"));
        Assert.Equal("Api", finding.Explanation.Arg("target"));
    }

    [Theory]
    [InlineData(NodeType.Database, NodeType.Api)]
    [InlineData(NodeType.Service, NodeType.Client)]
    [InlineData(NodeType.Cdn, NodeType.Database)]
    [InlineData(NodeType.Scheduler, NodeType.Database)]
    public void EverySentence_CarriesBothNames(NodeType source, NodeType target)
    {
        _builder.Node("A", source).Node("B", target).Connect("A", "B");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        foreach (var text in new[] { finding.Message, finding.Explanation, finding.Suggestion })
        {
            Assert.Equal("A", text.Arg("source"));
            Assert.Equal("B", text.Arg("target"));
        }
    }
}
