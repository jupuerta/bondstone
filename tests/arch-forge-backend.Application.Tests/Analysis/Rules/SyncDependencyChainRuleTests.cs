using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Domain.Enums;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

public class SyncDependencyChainRuleTests
{
    private readonly SyncDependencyChainRule _rule = new();
    private readonly GraphBuilder _builder = new();

    [Fact]
    public void ThreeComponentChain_IsAWarning()
    {
        _builder
            .Nodes(default, "API", "Orders", "Payments")
            .Connect("API", "Orders")
            .Connect("Orders", "Payments");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(RuleCodes.SyncChain, finding.Code);
        Assert.Equal(Area.Resilience, finding.Area);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.SYNC_CHAIN.long.message(count=3, node=API)", finding.Message.Describe());
    }

    [Fact]
    public void FiveComponentChain_EscalatesToCritical()
    {
        _builder.Nodes(default, "A", "B", "C", "D", "E")
            .Connect("A", "B").Connect("B", "C").Connect("C", "D").Connect("D", "E");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("5", finding.Message.Arg("count"));
    }

    [Fact]
    public void Chain_IsReportedOnceFromTheNodeThatStartsIt()
    {
        _builder.Nodes(default, "A", "B", "C", "D")
            .Connect("A", "B").Connect("B", "C").Connect("C", "D");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.SYNC_CHAIN.long.message(count=4, node=A)", finding.Message.Describe());
    }

    [Fact]
    public void Chain_ElementsInterleaveTheWalk()
    {
        _builder.Nodes(default, "A", "B", "C")
            .Connect("A", "B").Connect("B", "C");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(["A", "A->B", "B", "B->C", "C"], _builder.Names(finding.Elements));
    }

    [Fact]
    public void BranchingGraph_ReportsTheLongestBranch()
    {
        _builder.Nodes(default, "A", "B", "C", "D")
            .Connect("A", "B")
            .Connect("A", "C").Connect("C", "D");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(["A", "A->C", "C", "C->D", "D"], _builder.Names(finding.Elements));
    }

    [Fact]
    public void AsynchronousHopBreaksTheChain()
    {
        _builder.Nodes(default, "A", "B", "C", "D")
            .Connect("A", "B")
            .Connect("B", "C", synchronous: false)
            .Connect("C", "D");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void EdgesInACycle_AreExcludedFromTheChain()
    {
        _builder.Nodes(default, "A", "B", "C")
            .Connect("A", "B").Connect("B", "C").Connect("C", "A");

        var graph = _builder.Build();
        graph.Facts.Add(new CyclicConnectionFact(_builder.EdgeId("C", "A")));

        var finding = Assert.Single(_rule.Evaluate(graph).Findings);

        Assert.Equal(["A", "A->B", "B", "B->C", "C"], _builder.Names(finding.Elements));
    }

    [Fact]
    public void ChainThatOnlyExistsThroughACycle_IsNotReported()
    {
        _builder.Nodes(default, "A", "B")
            .Connect("A", "B").Connect("B", "A");

        var graph = _builder.Build();
        graph.Facts.Add(new CyclicConnectionFact(_builder.EdgeId("A", "B")));
        graph.Facts.Add(new CyclicConnectionFact(_builder.EdgeId("B", "A")));

        var result = _rule.Evaluate(graph);

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void DiamondGraph_IsWalkedOnceNotOncePerPath()
    {
        _builder.Node("Start");
        for (var i = 0; i < 24; i++)
        {
            _builder.Nodes(default, $"L{i}", $"R{i}", $"Join{i}");
            _builder.Connect(i == 0 ? "Start" : $"Join{i - 1}", $"L{i}");
            _builder.Connect(i == 0 ? "Start" : $"Join{i - 1}", $"R{i}");
            _builder.Connect($"L{i}", $"Join{i}");
            _builder.Connect($"R{i}", $"Join{i}");
        }

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("49", finding.Message.Arg("count"));
    }

    [Fact]
    public void ShortChain_IsApplicableAndPasses()
    {
        _builder.Nodes(default, "A", "B").Connect("A", "B");

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void NoSynchronousEdge_IsNotApplicable()
    {
        _builder.Nodes(default, "A", "B").Connect("A", "B", synchronous: false);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }


    [Fact]
    public void TheCanonicalReadPath_IsNotAChain()
    {
        _builder
            .Node("Web", NodeType.Client).Node("Edge", NodeType.Cdn).Node("Gw", NodeType.Gateway)
            .Node("Api", NodeType.Api).Node("Svc", NodeType.Service)
            .Node("Cache", NodeType.Cache).Node("Db", NodeType.Database)
            .Connect("Web", "Edge").Connect("Edge", "Gw").Connect("Gw", "Api")
            .Connect("Api", "Svc").Connect("Svc", "Cache").Connect("Cache", "Db");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void TheCalibrationExample_StillCountsThree()
    {
        _builder
            .Node("Api", NodeType.Api).Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Api", "Orders").Connect("Orders", "Payments");

        var bare = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
        Assert.Equal("finding.SYNC_CHAIN.long.message(count=3, node=Api)", bare.Message.Describe());

        var layered = new GraphBuilder()
            .Node("Edge", NodeType.Cdn).Node("Gw", NodeType.Gateway)
            .Node("Api", NodeType.Api).Nodes(NodeType.Service, "Orders", "Payments")
            .Node("Cache", NodeType.Cache).Node("Db", NodeType.Database)
            .Connect("Edge", "Gw").Connect("Gw", "Api")
            .Connect("Api", "Orders").Connect("Orders", "Payments")
            .Connect("Payments", "Cache").Connect("Cache", "Db");

        var finding = Assert.Single(_rule.Evaluate(layered.Build()).Findings);

        Assert.Equal("finding.SYNC_CHAIN.long.message(count=3, node=Api)", finding.Message.Describe());
    }

    [Fact]
    public void TheFindingStartsAtTheFirstComponentItCounts()
    {
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Node("Api", NodeType.Api).Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("LB", "Api").Connect("Api", "Orders").Connect("Orders", "Payments");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.SYNC_CHAIN.long.message(count=3, node=Api)", finding.Message.Describe());
        Assert.Equal(_builder.NodeElement("Api"), finding.Elements[0]);
        Assert.DoesNotContain(_builder.NodeElement("LB"), finding.Elements);
    }

    [Fact]
    public void TwoDoorsIntoTheSameChain_AreOneFinding()
    {
        _builder
            .Nodes(NodeType.Gateway, "Gw1", "Gw2")
            .Node("Api", NodeType.Api).Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Gw1", "Api").Connect("Gw2", "Api")
            .Connect("Api", "Orders").Connect("Orders", "Payments");

        Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void AChainOfInfrastructureAlone_IsNeverAChain()
    {
        _builder
            .Node("Edge", NodeType.Cdn).Node("Gw", NodeType.Gateway).Node("LB", NodeType.LoadBalancer)
            .Node("Cache", NodeType.Cache).Node("Db", NodeType.Database)
            .Connect("Edge", "Gw").Connect("Gw", "LB").Connect("LB", "Cache").Connect("Cache", "Db");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void SomebodyElsesServiceIsALinkLikeAnyOther()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Auth", NodeType.IdentityProvider)
            .Node("Stripe", NodeType.ExternalService)
            .Connect("Api", "Auth").Connect("Auth", "Stripe");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.SYNC_CHAIN.long.message(count=3, node=Api)", finding.Message.Describe());
    }

    [Fact]
    public void AProvidersOwnHop_IsNotALinkOfYourChain()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Stripe", NodeType.ExternalService)
            .Node("Maps", NodeType.ExternalService)
            .Connect("Api", "Stripe").Connect("Stripe", "Maps");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }


    [Fact]
    public void TheTelemetryDoesNotJoinTwoChainsIntoOne()
    {
        _builder
            .Nodes(NodeType.Service, "A", "B", "C", "D")
            .Node("Obs", NodeType.Observability)
            .Connect("A", "B").Connect("B", "Obs")
            .Connect("Obs", "C").Connect("C", "D");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void InstrumentingAChain_ChangesNothingAboutIt()
    {
        var bare = new GraphBuilder()
            .Node("Api", NodeType.Api).Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Api", "Orders").Connect("Orders", "Payments");

        var observed = new GraphBuilder()
            .Node("Api", NodeType.Api).Nodes(NodeType.Service, "Orders", "Payments")
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Orders").Connect("Orders", "Payments")
            .Connect("Obs", "Api").Connect("Orders", "Obs").Connect("Obs", "Payments");

        Assert.Equal(
            _rule.Evaluate(bare.Build()).Findings.Single().Message.Describe(),
            _rule.Evaluate(observed.Build()).Findings.Single().Message.Describe());
    }


    [Fact]
    public void AChainThatEndsInARead_IsNotToldToCutItWithAQueue()
    {
        _builder
            .Node("Api", NodeType.Api).Nodes(NodeType.Service, "B", "C").Node("Db", NodeType.Database)
            .Connect("Api", "B").Connect("B", "C").Connect("C", "Db", protocol: ProtocolType.Sql);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.SYNC_CHAIN.long.suggestion.read", finding.Suggestion.Describe());
        Assert.Equal("finding.SYNC_CHAIN.long.message(count=3, node=Api)", finding.Message.Describe());
        Assert.Equal("finding.SYNC_CHAIN.long.explanation", finding.Explanation.Describe());
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(["Api", "Api->B", "B", "B->C", "C", "C->Db", "Db"], _builder.Names(finding.Elements));
    }

    [Fact]
    public void DeclaringTheLastHopAWrite_BringsTheQueueBack()
    {
        _builder
            .Node("Api", NodeType.Api).Nodes(NodeType.Service, "B", "C").Node("Db", NodeType.Database)
            .Connect("Api", "B").Connect("B", "C").Connect("C", "Db", protocol: ProtocolType.Sql, access: "write");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.SYNC_CHAIN.long.suggestion", finding.Suggestion.Describe());
    }

    [Fact]
    public void AChainOfServices_KeepsTheQueueAdvice()
    {
        _builder
            .Node("Api", NodeType.Api).Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Api", "Orders").Connect("Orders", "Payments");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.SYNC_CHAIN.long.suggestion", finding.Suggestion.Describe());
    }

    [Theory]
    [InlineData(NodeType.Database, true)]
    [InlineData(NodeType.ObjectStorage, true)]
    [InlineData(NodeType.Cache, true)]
    [InlineData(NodeType.Service, false)]
    [InlineData(NodeType.ExternalService, false)]
    public void WhatEndsTheChain_DecidesTheVariant(NodeType tail, bool read)
    {
        _builder
            .Node("Api", NodeType.Api).Nodes(NodeType.Service, "B", "C").Node("End", tail)
            .Connect("Api", "B").Connect("B", "C").Connect("C", "End");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(
            read ? "finding.SYNC_CHAIN.long.suggestion.read" : "finding.SYNC_CHAIN.long.suggestion",
            finding.Suggestion.Describe());
    }

    [Fact]
    public void AReadThroughACache_IsStillARead()
    {
        _builder
            .Node("Api", NodeType.Api).Nodes(NodeType.Service, "B", "C")
            .Node("Cache", NodeType.Cache).Node("Db", NodeType.Database)
            .Connect("Api", "B").Connect("B", "C").Connect("C", "Cache").Connect("Cache", "Db", protocol: ProtocolType.Sql);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.SYNC_CHAIN.long.suggestion.read", finding.Suggestion.Describe());
        Assert.Equal("Db", _builder.Names(finding.Elements).Last());
    }

    [Fact]
    public void WorstCase_IsToldToShortenItsChainNotToQueueIt()
    {
        var finding = ReferenceArchitecture.Analyze("worst-case").Findings
            .Single(f => f.Code == RuleCodes.SyncChain && f.Severity != Severity.Good);

        Assert.Equal("finding.SYNC_CHAIN.long.suggestion.read", finding.Suggestion.Describe());
        Assert.Equal("finding.SYNC_CHAIN.long.message(count=3, node=Orders)", finding.Message.Describe());
    }
}
