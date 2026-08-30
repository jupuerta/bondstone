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

    // QA 30-08 §3.1: five arrows that cannot be asynchronous. Unticking the box on them is not a
    // design, it is the drawing contradicting itself — and the cheapest way there was of making a
    // dependency vanish from every rule that reads «synchronous».
    [Theory]
    [InlineData(NodeType.Client, NodeType.Gateway, null, "asyncClient")]
    [InlineData(NodeType.Gateway, NodeType.Api, null, "asyncRouter")]
    [InlineData(NodeType.LoadBalancer, NodeType.Api, null, "asyncRouter")]
    [InlineData(NodeType.Cdn, NodeType.Api, null, "asyncRouter")]
    [InlineData(NodeType.Api, NodeType.IdentityProvider, null, "asyncIdentity")]
    [InlineData(NodeType.Cache, NodeType.Database, null, "asyncCacheMiss")]
    [InlineData(NodeType.Service, NodeType.Database, "read", "asyncRead")]
    // Undeclared is the default and the model counts it as a read (QA 30-08, §10.3 point 6): the
    // commonest drawing of all used to be the one nobody checked.
    [InlineData(NodeType.Service, NodeType.Database, null, "asyncRead")]
    [InlineData(NodeType.Service, NodeType.ObjectStorage, null, "asyncRead")]
    public void An_arrow_that_cannot_stop_waiting_is_critical_when_it_says_it_does(NodeType source, NodeType target, string? access, string variant)
    {
        _builder.Node("A", source).Node("B", target).Connect("A", "B", synchronous: false, access: access);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal($"finding.IMPLAUSIBLE_DEPENDENCY.{variant}.message", finding.Message.Key());
        Assert.Equal("A", finding.Message.Arg("source"));
        Assert.Equal("B", finding.Message.Arg("target"));
    }

    [Theory]
    [InlineData(NodeType.Service, NodeType.Database, "write")]
    [InlineData(NodeType.Api, NodeType.Queue, null)]
    [InlineData(NodeType.Service, NodeType.Service, null)]
    public void Not_waiting_is_still_allowed_where_the_work_can_wait(NodeType source, NodeType target, string? access)
    {
        _builder.Node("A", source).Node("B", target).Connect("A", "B", synchronous: false, access: access);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    // QA 30-08 §10.3, point 7. A door's callers are outside watching a screen: if every call it makes
    // says «I do not wait», there is no answer to give — and P19 measured the model serving 100 % of
    // them. Queueing the work is the honest version of the same shape, which is what `backlog`
    // teaches, so a broker among its calls answers the question.
    [Fact]
    public void A_door_that_waits_for_nobody_is_the_drawing_talking()
    {
        _builder.Node("Door", NodeType.Api, """{"requestsPerSecond":100}""").Node("Core", NodeType.Service)
            .Connect("Door", "Core", synchronous: false);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.IMPLAUSIBLE_DEPENDENCY.asyncDoor.message", finding.Message.Key());
        Assert.Equal("Door", finding.Message.Arg("source"));
    }

    // The gap this variant fills is the Api: a gateway, a balancer or a CDN in the same shape already
    // have a sentence of their own, and it is the more specific of the two — routing without waiting
    // is impossible whether or not the box behind is the only one it calls.
    [Theory]
    [InlineData(NodeType.Gateway)]
    [InlineData(NodeType.LoadBalancer)]
    [InlineData(NodeType.Cdn)]
    public void A_router_in_the_same_shape_keeps_its_own_sentence(NodeType router)
    {
        _builder.Node("Door", router, """{"requestsPerSecond":100}""").Node("Core", NodeType.Service)
            .Connect("Door", "Core", synchronous: false);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.IMPLAUSIBLE_DEPENDENCY.asyncRouter.message", finding.Message.Key());
    }

    [Fact]
    public void A_door_that_queues_the_work_has_accepted_it()
    {
        _builder.Node("Storefront", NodeType.Api, """{"requestsPerSecond":500}""")
            .Node("Jobs", NodeType.Queue).Node("Worker", NodeType.Worker)
            .Connect("Storefront", "Jobs", synchronous: false)
            .Connect("Jobs", "Worker", synchronous: false);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void One_synchronous_call_is_enough_to_be_answering_somebody()
    {
        _builder.Node("Door", NodeType.Api, """{"requestsPerSecond":100}""")
            .Nodes(NodeType.Service, "Core", "Mailer")
            .Connect("Door", "Core", timeoutSeconds: 3)
            .Connect("Door", "Mailer", synchronous: false);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    // A box behind a door is not a door: what does not wait there is ASYNC_NO_QUEUE's question.
    [Fact]
    public void What_hangs_behind_a_door_is_not_asked_this()
    {
        _builder.Node("Edge", NodeType.Gateway).Node("Api", NodeType.Api).Node("Core", NodeType.Service)
            .Connect("Edge", "Api", timeoutSeconds: 3)
            .Connect("Api", "Core", synchronous: false);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    // QA 30-08 §1.6 and 7.4: a router routes. It does not run SQL — calling the reporting panel a
    // gateway was how exposed-database was passed without moving a wire — and it has nothing to
    // answer with, so a fallback on its call is the drawing promising a plan B that is not there.
    [Fact]
    public void A_router_that_runs_SQL_or_promises_a_fallback_is_the_drawing_talking()
    {
        _builder.Node("Edge", NodeType.Gateway).Node("Front", NodeType.LoadBalancer).Node("Api", NodeType.Api).Node("Db", NodeType.Database)
            .Connect("Edge", "Db", timeoutSeconds: 3)
            .Connect("Front", "Api", timeoutSeconds: 3, fallback: true)
            .Connect("Api", "Db", timeoutSeconds: 3, fallback: true);

        var findings = _rule.Evaluate(_builder.Build()).Findings;

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(Severity.Warning, f.Severity));
        Assert.Contains(findings, f => f.Message.Key() == "finding.IMPLAUSIBLE_DEPENDENCY.routerToDatabase.message" && f.Message.Arg("source") == "Edge");
        Assert.Contains(findings, f => f.Message.Key() == "finding.IMPLAUSIBLE_DEPENDENCY.routerFallback.message" && f.Message.Arg("source") == "Front");
    }

    [Fact]
    public void The_same_arrows_waiting_are_not_implausible()
    {
        _builder.Node("Users", NodeType.Client).Node("Gw", NodeType.Gateway).Node("Api", NodeType.Api)
            .Node("IdP", NodeType.IdentityProvider).Node("Cache", NodeType.Cache).Node("Db", NodeType.Database)
            .Connect("Users", "Gw").Connect("Gw", "Api").Connect("Api", "IdP")
            .Connect("Api", "Cache").Connect("Cache", "Db").Connect("Api", "Db", access: "read");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }


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
            .Connect("Edge", "Db");

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
            .Connect("Api", "Cache").Connect("Cache", "Db");

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
            .Connect("Gw", "Auth").Connect("Api", "Svc").Connect("Svc", "Cache").Connect("Cache", "Db")
            .Connect("Api", "Queue", synchronous: false)
            .Connect("Queue", "Worker", synchronous: false)
            .Connect("Worker", "Db").Connect("Worker", "Files").Connect("Cron", "Worker")
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
