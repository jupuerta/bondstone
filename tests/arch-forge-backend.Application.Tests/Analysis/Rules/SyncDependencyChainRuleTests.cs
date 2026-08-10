using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
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
        Assert.Equal("'API' inicia una cadena de dependencias síncronas de 3 componentes.", finding.Message);
    }

    [Fact]
    public void FiveComponentChain_EscalatesToCritical()
    {
        // The cut at 5 is the literal example of §8.1 of the ideas document (D4.2).
        _builder.Nodes(default, "A", "B", "C", "D", "E")
            .Connect("A", "B").Connect("B", "C").Connect("C", "D").Connect("D", "E");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Contains("5 componentes", finding.Message);
    }

    [Fact]
    public void Chain_IsReportedOnceFromTheNodeThatStartsIt()
    {
        _builder.Nodes(default, "A", "B", "C", "D")
            .Connect("A", "B").Connect("B", "C").Connect("C", "D");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Contains("'A' inicia", finding.Message);
        Assert.Contains("4 componentes", finding.Message);
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
        Assert.Contains("49 componentes", finding.Message);
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
}
