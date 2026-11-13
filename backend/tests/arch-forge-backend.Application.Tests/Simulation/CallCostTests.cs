using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Simulation;
using bondstone_backend.Application.Tests.Analysis.Fixtures;
using Xunit;

namespace bondstone_backend.Application.Tests.Simulation;

public class CallCostTests
{
    private static CostInputs WithStates(params (Guid Id, HealthState State)[] states) => new(
        states.ToDictionary(s => s.Id, s => s.State),
        new Dictionary<FindingElement, int>(),
        new HashSet<Guid>());

    private static CostInputs WithLatency(FindingElement element, int ms) => new(
        new Dictionary<Guid, HealthState>(),
        new Dictionary<FindingElement, int> { [element] = ms },
        new HashSet<Guid>());


    [Theory]
    [InlineData("minimal-api-db", 27)]
    [InlineData("gateway-microservices", 57)]
    [InlineData("resilient-async", 39)]
    [InlineData("worst-case", 72)]
    public void NominalCostOfTheFourReferenceArchitectures(string fixtureName, int expected)
    {
        var graph = ReferenceArchitecture.Graph(fixtureName);

        var cost = CallCost.Of(graph, CostInputs.Nominal);

        Assert.Equal(expected, cost.WorstFrom(graph.RootNodes));
    }

    [Fact]
    public void NominalRegime_AmplifiesNothing()
    {
        var graph = ReferenceArchitecture.Graph("worst-case");

        var cost = CallCost.Of(graph, CostInputs.Nominal);

        Assert.Equal(1m, cost.WorstAmplificationFrom(graph.RootNodes));
    }

    [Fact]
    public void AnAsynchronousEdgeCostsTheCallerNothing()
    {
        var graph = ReferenceArchitecture.Graph("resilient-async");

        var cost = CallCost.Of(graph, CostInputs.Nominal);

        Assert.Equal(0, cost.Of(graph.Connections.Single(c => c.Id == graph.EdgeId("Api1", "Queue"))).LatencyMs);
    }


    [Fact]
    public void ADeadDependencyCostsItsTimeout_NotTheSubtree()
    {
        var graph = ReferenceArchitecture.Graph("minimal-api-db");

        var cost = CallCost.Of(graph, WithStates((graph.NodeId("Db"), HealthState.Down)));

        Assert.Equal(5_005, cost.WorstFrom(graph.RootNodes));
    }

    [Fact]
    public void WithoutATimeout_ADeadDependencyCostsThirtySeconds()
    {
        var graph = ReferenceArchitecture.Graph("worst-case");

        var cost = CallCost.Of(graph, WithStates((graph.NodeId("Payments"), HealthState.Down)));

        Assert.Equal(LatencyModel.UnboundedWait, cost.Of(EdgeOf(graph, "Orders", "Payments")).LatencyMs);
    }

    [Fact]
    public void RetriesMultiplyTheWait()
    {
        var graph = ReferenceArchitecture.Graph("worst-case");

        var cost = CallCost.Of(graph, WithStates((graph.NodeId("Shipping"), HealthState.Down)));

        Assert.Equal(20_000, cost.Of(EdgeOf(graph, "Orders", "Shipping")).LatencyMs);
        Assert.Equal(4m, cost.Of(EdgeOf(graph, "Orders", "Shipping")).Amplification);
    }

    [Fact]
    public void WithoutATimeoutTheRetriesDoNotEvenHappen()
    {
        var graph = ReferenceArchitecture.Graph("gateway-microservices");

        var cost = CallCost.Of(graph, WithStates((graph.NodeId("Payments"), HealthState.Down)));

        Assert.Equal(1m, cost.Of(EdgeOf(graph, "Orders", "Payments")).Amplification);
    }

    [Fact]
    public void TheCircuitBreakerCutsTheCallAtOnceAndTheAmplificationWithIt()
    {
        var graph = ReferenceArchitecture.Graph("resilient-async");

        var cost = CallCost.Of(graph, WithStates((graph.NodeId("Db"), HealthState.Down)));

        Assert.Equal(0, cost.Of(EdgeOf(graph, "Cache", "Db")).LatencyMs);
        Assert.Equal(1m, cost.Of(EdgeOf(graph, "Cache", "Db")).Amplification);
    }

    [Fact]
    public void FailingFastIsFasterThanWorking()
    {
        var graph = ReferenceArchitecture.Graph("resilient-async");

        var cost = CallCost.Of(graph, WithStates((graph.NodeId("Db"), HealthState.Down)));

        Assert.Equal(17, cost.WorstFrom(graph.RootNodes));
    }


    [Fact]
    public void InjectedLatencyOverTheTimeout_MakesTheCallExpire()
    {
        var graph = ReferenceArchitecture.Graph("worst-case");
        var edge = EdgeOf(graph, "Orders", "Shipping");

        var cost = CallCost.Of(graph, WithLatency(FindingElement.Connection(edge.Id), 8_000));

        Assert.True(cost.Of(edge).Expires);
        Assert.Equal(20_000, cost.Of(edge).LatencyMs);
    }

    [Fact]
    public void AnExpiredCallBlowsTheTimeoutOfWhoeverCalledIt()
    {
        var graph = ReferenceArchitecture.Graph("worst-case");
        var shipping = EdgeOf(graph, "Orders", "Shipping");

        var cost = CallCost.Of(graph, WithLatency(FindingElement.Connection(shipping.Id), 8_000));

        Assert.True(cost.Of(EdgeOf(graph, "Gateway", "Orders")).Expires);
        Assert.Equal(15_005, cost.WorstFrom(graph.RootNodes));
    }

    [Fact]
    public void InjectedLatencyUnderTheTimeout_JustAddsUp()
    {
        var graph = ReferenceArchitecture.Graph("minimal-api-db");
        var edge = EdgeOf(graph, "Api", "Db");

        var cost = CallCost.Of(graph, WithLatency(FindingElement.Connection(edge.Id), 1_000));

        Assert.False(cost.Of(edge).Expires);
        Assert.Equal(1_027, cost.WorstFrom(graph.RootNodes));
    }


    [Fact]
    public void TheEdgeThatClosesACycleContributesNothing()
    {
        var graph = ReferenceArchitecture.Graph("worst-case");

        var cost = CallCost.Of(graph, CostInputs.Nominal);

        Assert.True(cost.Of(EdgeOf(graph, "Inventory", "Orders")).CycleCut);
        Assert.Equal(0, cost.Of(EdgeOf(graph, "Inventory", "Orders")).LatencyMs);
        Assert.Equal(10, cost.Of(graph.NodeId("Inventory")).LatencyMs);
    }

    [Fact]
    public void TheCycleCutIsIndependentOfTheOrderTheGraphWasWrittenIn()
    {
        var (nodes, connections) = ReferenceArchitecture.Load("worst-case");
        var shuffled = new ArchitectureGraph(
            nodes.AsEnumerable().Reverse().ToList(),
            connections.AsEnumerable().Reverse().ToList());

        var cost = CallCost.Of(shuffled, CostInputs.Nominal);

        Assert.Equal(72, cost.WorstFrom(shuffled.RootNodes));
        Assert.True(cost.Of(EdgeOf(shuffled, "Inventory", "Orders")).CycleCut);
    }

    private static Domain.Entities.Connection EdgeOf(ArchitectureGraph graph, string source, string target) =>
        graph.Connections.Single(c => c.Id == graph.EdgeId(source, target));
}
