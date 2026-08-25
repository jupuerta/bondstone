using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation;

public class CriticalPathTests
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
    [InlineData("minimal-api-db", "Api,Db")]
    [InlineData("gateway-microservices", "Gateway,Orders,Payments,Db")]
    [InlineData("resilient-async", "LB,Api1,Cache,Db")]
    [InlineData("worst-case", "Gateway,Orders,Shipping,Notifications,Db")]
    public void TheNominalPathOfTheFourReferenceArchitectures(string fixtureName, string expected)
    {
        var graph = ReferenceArchitecture.Graph(fixtureName);

        var path = CallCost.Of(graph, CostInputs.Nominal).CriticalPathFrom(graph.RootNodes);

        Assert.Equal(expected.Split(','), path.Select(hop => graph.NodesById[hop.NodeId].Name));
    }

    [Fact]
    public void TheCascadeAddsUpToTheNumberThePanelAlreadyPrinted()
    {
        var graph = ReferenceArchitecture.Graph("minimal-api-db");

        var cost = CallCost.Of(graph, CostInputs.Nominal);
        var path = cost.CriticalPathFrom(graph.RootNodes);

        var api = path[0];
        Assert.Null(api.ConnectionId);
        Assert.Equal(0, api.NetworkMs);
        Assert.Equal(LatencyModel.Api, api.ProcessingMs);
        Assert.Equal(5, api.CumulativeMs);

        var db = path[1];
        Assert.Equal(graph.EdgeId("Api", "Db"), db.ConnectionId);
        Assert.Equal(LatencyModel.Sql, db.NetworkMs);
        Assert.Equal(LatencyModel.Database, db.ProcessingMs);
        Assert.Equal(22, db.TotalMs);
        Assert.Equal(27, db.CumulativeMs);
        Assert.Equal(cost.WorstFrom(graph.RootNodes), db.CumulativeMs);
    }

    [Fact]
    public void TheThirdSegmentIsWhatBeingFullCosts()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, "{\"requestsPerSecond\":300}")
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", protocol: ProtocolType.Sql)
            .Build();

        var load = LoadModel.Of(graph);
        var cost = CallCost.Of(graph, CostInputs.Nominal with { QueueingFactors = load.QueueingFactors });
        var path = cost.CriticalPathFrom(graph.RootNodes);

        Assert.Equal(2.5m, path[0].QueueingFactor);
        Assert.Equal(LatencyModel.Api, path[0].ProcessingMs);
        Assert.Equal(8, path[0].QueueingMs);

        Assert.Equal(30m, path[1].QueueingFactor);
        Assert.Equal(LatencyModel.Database, path[1].ProcessingMs);
        Assert.Equal(580, path[1].QueueingMs);
        Assert.Equal(LatencyModel.Sql, path[1].NetworkMs);

        Assert.Equal(615, path[^1].CumulativeMs);
        Assert.Equal(cost.WorstFrom(graph.RootNodes), path[^1].CumulativeMs);
    }


    [Fact]
    public void AnAsynchronousCallIsNeverOnThePath()
    {
        var graph = ReferenceArchitecture.Graph("resilient-async");

        var path = CallCost.Of(graph, CostInputs.Nominal).CriticalPathFrom(graph.RootNodes);

        Assert.DoesNotContain(graph.EdgeId("Api1", "Queue"), path.Select(hop => hop.ConnectionId));
    }

    [Fact]
    public void TheEdgeThatClosesACycleIsNeverOnThePath()
    {
        var graph = ReferenceArchitecture.Graph("worst-case");

        var path = CallCost.Of(graph, CostInputs.Nominal).CriticalPathFrom(graph.RootNodes);

        Assert.DoesNotContain(graph.EdgeId("Inventory", "Orders"), path.Select(hop => hop.ConnectionId));
        Assert.Equal(path.Select(hop => hop.NodeId).Distinct(), path.Select(hop => hop.NodeId));
    }

    [Fact]
    public void ThePathStopsAtTheCallThatFailed()
    {
        var graph = ReferenceArchitecture.Graph("minimal-api-db");

        var cost = CallCost.Of(graph, WithStates((graph.NodeId("Db"), HealthState.Down)));
        var path = cost.CriticalPathFrom(graph.RootNodes);

        var db = Assert.Single(path, hop => hop.Outcome != HopOutcome.Completed);
        Assert.Equal(HopOutcome.Unreachable, db.Outcome);
        Assert.Equal(0, db.ProcessingMs);
        Assert.Equal(0, db.NetworkMs);
        Assert.Equal(5_000, db.TotalMs);
        Assert.Equal(5_000, db.TimeoutMs);
        Assert.Equal(5_005, path[^1].CumulativeMs);
        Assert.Same(path[^1], db);
    }

    [Fact]
    public void TheBreakerCutsTheCallToZeroAndTheCallLeavesThePath()
    {
        var graph = ReferenceArchitecture.Graph("resilient-async");

        var path = CallCost.Of(graph, WithStates((graph.NodeId("Db"), HealthState.Down)))
            .CriticalPathFrom(graph.RootNodes);

        Assert.Equal(["LB", "Api1", "Cache"], path.Select(hop => graph.NodesById[hop.NodeId].Name));
        Assert.Equal(17, path[^1].CumulativeMs);
    }

    [Fact]
    public void ThereIsNoPathWhenThereIsNoDoorLeftStanding()
    {
        var graph = ReferenceArchitecture.Graph("minimal-api-db");

        var path = CallCost.Of(graph, CostInputs.Nominal).CriticalPathFrom([]);

        Assert.Empty(path);
    }


    [Fact]
    public void AnExpiredCallPaintsWhatWasChargedAndAnnotatesWhatItWouldHaveCost()
    {
        var graph = ReferenceArchitecture.Graph("worst-case");
        var shipping = graph.EdgeId("Orders", "Shipping");

        var path = CallCost.Of(graph, WithLatency(FindingElement.Connection(shipping), 8_000))
            .CriticalPathFrom(graph.RootNodes);

        var orders = path[^1];
        Assert.Equal(HopOutcome.Expired, orders.Outcome);
        Assert.Equal(graph.EdgeId("Gateway", "Orders"), orders.ConnectionId);
        Assert.Equal(15_000, orders.CallMs);
        Assert.Equal(20_015, orders.RawCallMs);
        Assert.Equal(5_000, orders.TimeoutMs);
        Assert.Equal(2, orders.Retries);
        Assert.Equal(15_005, orders.CumulativeMs);
    }

    [Fact]
    public void ACallWithNoTimeoutIsMarkedAsTheThirtySecondsItIs()
    {
        var graph = ReferenceArchitecture.Graph("worst-case");

        var path = CallCost.Of(graph, WithStates((graph.NodeId("Payments"), HealthState.Down)))
            .CriticalPathFrom(graph.RootNodes);

        var orders = path[^1];
        Assert.Equal(HopOutcome.Expired, orders.Outcome);
        Assert.Equal(LatencyModel.UnboundedWait + 15, orders.RawCallMs);

        var unbounded = CallCost.Of(graph, WithStates((graph.NodeId("Payments"), HealthState.Down)))
            .CriticalPathFrom([graph.NodesById[graph.NodeId("Orders")]]);

        var payments = unbounded[^1];
        Assert.Null(payments.TimeoutMs);
        Assert.Equal(0, payments.Retries);
        Assert.Equal(LatencyModel.UnboundedWait, payments.TotalMs);
    }


    [Theory]
    [InlineData("minimal-api-db")]
    [InlineData("resilient-async")]
    [InlineData("gateway-microservices")]
    [InlineData("worst-case")]
    public void EveryBarReconstructsItselfAndThePathReconstructsTheHeadline(string fixtureName)
    {
        var graph = ReferenceArchitecture.Graph(fixtureName);

        foreach (var scenario in Scenarios(graph))
        {
            var result = new SimulationEngine().Run(graph, scenario);
            var path = result.CriticalPath;

            Assert.NotEmpty(path);

            var cumulative = 0;
            foreach (var hop in path)
            {
                var parts = hop.Outcome == HopOutcome.Completed
                    ? hop.NetworkMs + hop.ProcessingMs + hop.QueueingMs + hop.InjectedMs
                    : hop.CallMs;

                Assert.Equal(parts, hop.TotalMs);

                cumulative += hop.TotalMs;
                Assert.Equal(cumulative, hop.CumulativeMs);
            }

            Assert.Equal(result.Metrics.FaultedLatencyMs, path[^1].CumulativeMs);

            foreach (var (previous, hop) in path.Zip(path.Skip(1)))
            {
                var edge = graph.Connections.Single(c => c.Id == hop.ConnectionId);
                Assert.Equal(previous.NodeId, edge.SourceNodeId);
                Assert.Equal(hop.NodeId, edge.TargetNodeId);
            }
        }
    }

    [Fact]
    public void WithNoScenarioThePathIsTheNominalOne()
    {
        foreach (var name in new[] { "minimal-api-db", "resilient-async", "gateway-microservices", "worst-case" })
        {
            var graph = ReferenceArchitecture.Graph(name);
            var result = new SimulationEngine().Run(graph, SimulationScenario.Empty);

            Assert.Equal(result.Metrics.NominalLatencyMs, result.CriticalPath[^1].CumulativeMs);
            Assert.Equal(
                CallCost.Of(graph, CostInputs.Nominal).CriticalPathFrom(graph.RootNodes).Select(h => h.NodeId),
                result.CriticalPath.Select(h => h.NodeId));
        }
    }

    [Fact]
    public void ThePathDoesNotDependOnTheOrderTheGraphWasWrittenIn()
    {
        var (nodes, connections) = ReferenceArchitecture.Load("worst-case");
        var graph = new ArchitectureGraph(nodes, connections);
        var shuffled = new ArchitectureGraph(
            nodes.AsEnumerable().Reverse().ToList(),
            connections.AsEnumerable().Reverse().ToList());

        Assert.Equal(
            CallCost.Of(graph, CostInputs.Nominal).CriticalPathFrom(graph.RootNodes),
            CallCost.Of(shuffled, CostInputs.Nominal).CriticalPathFrom(shuffled.RootNodes));
    }

    private static IEnumerable<SimulationScenario> Scenarios(ArchitectureGraph graph)
    {
        yield return SimulationScenario.Empty;

        foreach (var node in graph.Nodes.Where(n => !graph.RootNodes.Contains(n)))
            yield return new SimulationScenario([new Fault(ElementType.Node, node.Id, FaultKinds.Down, null)]);

        foreach (var edge in graph.Connections)
            yield return new SimulationScenario([new Fault(ElementType.Connection, edge.Id, FaultKinds.Down, null)]);
    }
}
