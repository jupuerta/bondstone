using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

public class CycleRuleTests
{
    private readonly CycleRule _rule = new();
    private readonly GraphBuilder _builder = new();

    [Fact]
    public void SynchronousCycle_IsCritical()
    {
        // A synchronous cycle is a deadlock waiting to happen (D4.x).
        _builder
            .Nodes(default, "Orders", "Payments", "Inventory")
            .Connect("Orders", "Payments")
            .Connect("Payments", "Inventory")
            .Connect("Inventory", "Orders");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(RuleCodes.Cycle, finding.Code);
        Assert.Equal(Area.Coupling, finding.Area);
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.CYCLE.detected.message(count=3, walk=Orders → Payments → Inventory → Orders)",
            finding.Message.Describe());
    }

    [Fact]
    public void CycleWithAnAsynchronousHop_IsOnlyAWarning()
    {
        // With a queue in between it is debatable but workable.
        _builder
            .Nodes(default, "Orders", "Payments", "Inventory")
            .Connect("Orders", "Payments")
            .Connect("Payments", "Inventory", synchronous: false)
            .Connect("Inventory", "Orders");

        Assert.Equal(Severity.Warning, Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Severity);
    }

    [Fact]
    public void TwoNodeCycle_IsDetected()
    {
        // D4.6: CIRCULAR_DEPENDENCY is subsumed - A -> B -> A is just a cycle of length 2.
        _builder
            .Nodes(default, "Orders", "Payments")
            .Connect("Orders", "Payments")
            .Connect("Payments", "Orders");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("2", finding.Message.Arg("count"));
    }

    [Fact]
    public void Cycle_IsReportedOnceInCanonicalForm()
    {
        // Without normalizing to the lowest id, the same cycle would produce one finding per node
        // and the order of findings would stop being deterministic (D4.x).
        _builder
            .Nodes(default, "A", "B", "C")
            .Connect("B", "C")
            .Connect("C", "A")
            .Connect("A", "B");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.CYCLE.detected.message(count=3, walk=A → B → C → A)", finding.Message.Describe());
    }

    [Fact]
    public void Cycle_ElementsInterleaveTheWalkAndCloseOnTheStartingNode()
    {
        _builder
            .Nodes(default, "A", "B")
            .Connect("A", "B")
            .Connect("B", "A");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(
            ["A", "A->B", "B", "B->A", "A"],
            _builder.Names(finding.Elements));
    }

    [Fact]
    public void Cycle_PublishesOneFactPerEdgeOfTheCycle()
    {
        // SYNC_CHAIN excludes these edges, which is what leaves it an acyclic graph to walk
        // (D5.4) and what keeps a cycle from also being reported as a chain (D4.5).
        _builder
            .Nodes(default, "A", "B", "C")
            .Connect("A", "B")
            .Connect("B", "C")
            .Connect("C", "A")
            .Connect("C", "B");

        var graph = _builder.Build();
        _rule.Evaluate(graph);

        var cyclicEdges = graph.Facts.OfType<CyclicConnectionFact>().Select(f => f.ConnectionId).ToHashSet();

        Assert.Contains(_builder.EdgeId("A", "B"), cyclicEdges);
        Assert.Contains(_builder.EdgeId("B", "C"), cyclicEdges);
        Assert.Contains(_builder.EdgeId("C", "A"), cyclicEdges);
    }

    [Fact]
    public void AcyclicGraph_IsApplicableAndPasses()
    {
        _builder
            .Nodes(default, "A", "B", "C")
            .Connect("A", "B")
            .Connect("B", "C")
            .Connect("A", "C");

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void GraphWithNoEdges_IsNotApplicable()
    {
        _builder.Nodes(default, "A", "B");

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }

    [Fact]
    public void TwoIndependentCycles_AreBothReported()
    {
        _builder
            .Nodes(default, "A", "B", "C", "D")
            .Connect("A", "B").Connect("B", "A")
            .Connect("C", "D").Connect("D", "C");

        Assert.Equal(2, _rule.Evaluate(_builder.Build()).Findings.Count);
    }
}
