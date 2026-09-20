using bondstone_backend.Application.Tests.TestDoubles;
using bondstone_backend.Application.Localization;
using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Analysis.Rules;
using bondstone_backend.Application.Tests.Analysis.Rules.Support;
using bondstone_backend.Domain.Enums;
using Xunit;

namespace bondstone_backend.Application.Tests.Analysis.Rules;

public class CycleRuleTests
{
    private readonly CycleRule _rule = new();
    private readonly GraphBuilder _builder = new();

    [Fact]
    public void SynchronousCycle_IsCritical()
    {
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
    public void AnAllSynchronousCycle_IsToldToBreakItWithAnAsynchronousHop()
    {
        _builder
            .Nodes(default, "Orders", "Payments", "Inventory")
            .Connect("Orders", "Payments")
            .Connect("Payments", "Inventory")
            .Connect("Inventory", "Orders");

        Assert.Equal(
            "finding.CYCLE.detected.suggestion.synchronous",
            Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Suggestion.Key());
    }

    [Fact]
    public void ACycleThatAlreadyHasAnAsynchronousHop_IsToldToDecoupleInstead()
    {
        _builder
            .Nodes(default, "Orders", "Payments", "Inventory")
            .Connect("Orders", "Payments")
            .Connect("Payments", "Inventory", synchronous: false)
            .Connect("Inventory", "Orders");

        Assert.Equal(
            "finding.CYCLE.detected.suggestion.decouple",
            Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Suggestion.Key());
    }

    [Fact]
    public void ACycleThroughAStore_IsAnOwnershipProblemAndSaysSo()
    {
        _builder
            .Node("Orders")
            .Node("Db", NodeType.Database)
            .Connect("Orders", "Db")
            .Connect("Db", "Orders");

        Assert.Equal(
            "finding.CYCLE.detected.suggestion.sharedData",
            Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Suggestion.Key());
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


    [Fact]
    public void SendingMetricsAndBeingScrapedBack_IsNotACycle()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Obs").Connect("Obs", "Api");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void ALoopOfTelemetryBoxes_IsNotACycleEither()
    {
        _builder
            .Nodes(NodeType.Observability, "O1", "O2", "O3")
            .Connect("O1", "O2").Connect("O2", "O3").Connect("O3", "O1");

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }

    [Fact]
    public void ARealCycleWithTelemetryHangingOffIt_IsStillReported()
    {
        _builder
            .Nodes(default, "A", "B")
            .Node("Obs", NodeType.Observability)
            .Connect("A", "B").Connect("B", "A")
            .Connect("Obs", "A").Connect("Obs", "B");

        Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
    }
}
