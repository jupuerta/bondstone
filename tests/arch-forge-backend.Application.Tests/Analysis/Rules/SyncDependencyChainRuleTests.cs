using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Domain.Enums;
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
        // ArchForge_decisiones_arquitectura_V1_1.md §3.1: API -> Orders -> Payments is length 3.
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
        // The cut at 5 is the literal example of §8.1 of the ideas document (D4.2).
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
        // D2.2: node, edge, node, edge... so the canvas highlights by iterating.
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
        // A long asynchronous chain is not a problem - it is what the analyzer recommends.
        _builder.Nodes(default, "A", "B", "C", "D")
            .Connect("A", "B")
            .Connect("B", "C", synchronous: false)
            .Connect("C", "D");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void EdgesInACycle_AreExcludedFromTheChain()
    {
        // D4.5: a cycle is already reported as a cycle. Without excluding those edges it would be
        // reported again as a chain, and the walk would not even be over a DAG (D5.4).
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
        // The shape that made V1's backtracking blow up: every diamond doubles the number of
        // paths. Twenty-four of them in a row is over sixteen million walks the old way, and
        // seventy-three nodes visited once this way (D5.4).
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

    // ---- V14 B3: the chain is measured in logic, not in boxes ----------------

    [Fact]
    public void TheCanonicalReadPath_IsNotAChain()
    {
        // T8: the read path of every layered web application, protected on every hop. It came out
        // a *Critical* of six components advising a queue in the middle of a read - which you
        // cannot do. Two of those six run logic; the other four are the layers the call crosses.
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
        // The sentence the threshold was set on in V1, and the one that must not move: three places
        // where business logic runs is a chain of three, with or without the palette around it.
        _builder
            .Node("Api", NodeType.Api).Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Api", "Orders").Connect("Orders", "Payments");

        var bare = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
        Assert.Equal("finding.SYNC_CHAIN.long.message(count=3, node=Api)", bare.Message.Describe());

        // And it stays three when the same three are drawn inside a realistic diagram. This is the
        // whole of B3 in one assertion: the layers around the chain are not links of it.
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
        // Naming the load balancer as the head of a chain of services it is not one of was the same
        // confusion said out loud, and it is what the highlighted walk starts from too.
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
        // Consequence of starting at the first component counted: without the guard, two gateways
        // in front of the same chain reported it twice, identically.
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
        // The degenerate end of the same argument: five layers in a row and nothing that decides
        // anything. There is no chain of calls here, there is one call crossing five things.
        _builder
            .Node("Edge", NodeType.Cdn).Node("Gw", NodeType.Gateway).Node("LB", NodeType.LoadBalancer)
            .Node("Cache", NodeType.Cache).Node("Db", NodeType.Database)
            .Connect("Edge", "Gw").Connect("Gw", "LB").Connect("LB", "Cache").Connect("Cache", "Db");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void SomebodyElsesServiceIsALinkLikeAnyOther()
    {
        // An ExternalService and an IdentityProvider are a hop out of your process however they are
        // drawn, so they count - which is the other half of the distinction, and the reason it is
        // not "components I deployed".
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
        // V14 B10. `Api -> Stripe -> Maps` is a chain of two from where this architect stands;
        // what Stripe does with the call is Stripe's chain. It used to count three and warn.
        _builder
            .Node("Api", NodeType.Api)
            .Node("Stripe", NodeType.ExternalService)
            .Node("Maps", NodeType.ExternalService)
            .Connect("Api", "Stripe").Connect("Stripe", "Maps");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    // ---- V14 B9: instrumentation is not a link either -------------------------

    [Fact]
    public void TheTelemetryDoesNotJoinTwoChainsIntoOne()
    {
        // What skipping instrumentation buys this rule now that B3 counts logic: two short chains
        // that only meet at the metrics sink - the services push, the collector scrapes - were read
        // as one chain of four and warned about. Neither half waits on the other.
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
        // And the durable form: the same three services, with and without telemetry all over them,
        // produce the same finding down to the sentence.
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
}
