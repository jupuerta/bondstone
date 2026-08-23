using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

/// <summary>
/// V14 §4.1. The rule that makes the sweep's opening table stop being silence: a database that
/// called the users scored 98 and was labelled solid, because nothing asked whether the two ends
/// of an arrow could talk to each other at all.
/// </summary>
public class ImplausibleDependencyRuleTests
{
    private readonly ImplausibleDependencyRule _rule = new();
    private readonly GraphBuilder _builder = new();

    // ---- The sweep's table, row by row ----------------------------------------

    [Theory]
    [InlineData(NodeType.Cdn, NodeType.Database, "cdn", Severity.Critical)]
    [InlineData(NodeType.Database, NodeType.Client, "store", Severity.Warning)]
    // The cron that fires at the users is caught by the client sentence, not the scheduler one:
    // whatever the source, "something of yours calls the browser" is the more specific claim,
    // and the card that says so is the one that helps.
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
        // The row the table opens with: `Cdn -> Database` over Sql scored 96 "solid", and
        // PROTOCOL_MISMATCH could not see it because the protocol *does* match the far end. There
        // is no reading in which an edge cache runs a query, so it is the one Critical of the four.
        _builder.Node("Edge", NodeType.Cdn).Node("Db", NodeType.Database)
            .Connect("Edge", "Db", protocol: ProtocolType.Sql);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.IMPLAUSIBLE_DEPENDENCY.cdn.message(source=Edge, target=Db)", finding.Message.Describe());
    }

    // ---- What is deliberately allowed -------------------------------------------

    [Theory]
    [InlineData(NodeType.Api)]
    [InlineData(NodeType.Gateway)]
    [InlineData(NodeType.LoadBalancer)]
    [InlineData(NodeType.Service)]
    [InlineData(NodeType.ObjectStorage)]
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
    [InlineData(NodeType.Gateway)]
    public void AClockFiringWork_IsWhatAClockIsFor(NodeType work)
    {
        _builder.Node("Cron", NodeType.Scheduler).Node("Work", work).Connect("Cron", "Work");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void ACacheReadingThroughToItsOrigin_IsNotAStoreOriginatingACall()
    {
        // The line that keeps this rule from contradicting CACHE_AS_STORE, which *asks* for a
        // cache to have an origin behind it. A Cache is a data store; it is not a system of
        // record, and the store sentence is about the latter.
        _builder.Node("Api", NodeType.Api).Node("Cache", NodeType.Cache).Node("Db", NodeType.Database)
            .Connect("Api", "Cache").Connect("Cache", "Db", protocol: ProtocolType.Sql);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void InstrumentationIsNotRead()
    {
        // V14 B9. A database pushing its metrics is not a database originating a call, and a
        // graph that is only instrumentation has nothing to check - not a clean bill of health.
        _builder.Node("Db", NodeType.Database).Node("Obs", NodeType.Observability)
            .Connect("Db", "Obs", synchronous: false).Connect("Obs", "Db", synchronous: false);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }

    [Fact]
    public void AThirdPartysOwnCalls_AreItsOwnDrawing()
    {
        // V14 B10. Where Stripe sends its traffic - to the users, to its own store - is not
        // this architect's arrow to get right.
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
        // The canonical read path, every arrow the right way round: a green tick, earned.
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
        // The two names travel as arguments, so the catalogue can say "reverse it: it is B that
        // reads from A" without knowing the rule.
        _builder.Node("Db", NodeType.Database).Node("Api", NodeType.Api).Connect("Db", "Api");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("Db", finding.Explanation.Arg("source"));
        Assert.Equal("Api", finding.Explanation.Arg("target"));
    }
}
