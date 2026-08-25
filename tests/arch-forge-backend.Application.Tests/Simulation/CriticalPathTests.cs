using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation;

/// <summary>
/// V17 D1. The argument of the <c>Math.Max</c>, which is the whole change: every figure asserted
/// here was already being computed by <see cref="CallCost"/> when only the maximum came out.
///
/// So the tests are of two kinds and the second is the one that matters. The first walks the four
/// reference architectures and checks the path is the one §5.2 and §8 of the plan worked out by
/// hand. The second checks the <strong>invariants</strong>: that the segments of a bar add up to
/// the bar, that the last running total is the figure the metrics already reported, and that the
/// path ends where the walk ends. A cascade whose bars do not reconstruct its own total is a
/// drawing that lies, and it would lie quietly.
/// </summary>
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

    // -----------------------------------------------------------------------------------------
    // The four references, nominal: the same arithmetic CallCostTests pins as a single number.
    // -----------------------------------------------------------------------------------------

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
        // 27 ms = Api 5 + Sql 2 + Db 20, term by term and in the order the request pays them. Until
        // now the product could print the 27 and nothing else.
        var graph = ReferenceArchitecture.Graph("minimal-api-db");

        var cost = CallCost.Of(graph, CostInputs.Nominal);
        var path = cost.CriticalPathFrom(graph.RootNodes);

        var api = path[0];
        Assert.Null(api.ConnectionId);          // the entry point is not called from anywhere
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
        // DT.6, and the segment the product has never drawn: the database at 150 % of its capacity
        // takes 20 ms × 30 = 600, and the queueing factor is where 580 of the 615 go. Today that
        // only ever surfaces as a consequence inside a finding.
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, "{\"requestsPerSecond\":300}")
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", protocol: ProtocolType.Sql)
            .Build();

        var load = LoadModel.Of(graph);
        var cost = CallCost.Of(graph, CostInputs.Nominal with { QueueingFactors = load.QueueingFactors });
        var path = cost.CriticalPathFrom(graph.RootNodes);

        // The Api at 300/500 = 60 %: ×2,5, so 5 ms of processing become 13.
        Assert.Equal(2.5m, path[0].QueueingFactor);
        Assert.Equal(LatencyModel.Api, path[0].ProcessingMs);
        Assert.Equal(8, path[0].QueueingMs);

        // The database at 300/200 = 150 %: ×30, so 20 ms become 600.
        Assert.Equal(30m, path[1].QueueingFactor);
        Assert.Equal(LatencyModel.Database, path[1].ProcessingMs);
        Assert.Equal(580, path[1].QueueingMs);
        Assert.Equal(LatencyModel.Sql, path[1].NetworkMs);

        Assert.Equal(615, path[^1].CumulativeMs);
        Assert.Equal(cost.WorstFrom(graph.RootNodes), path[^1].CumulativeMs);
    }

    // -----------------------------------------------------------------------------------------
    // Where the path ends. Anexo A, decision 2: a condition, not three special cases.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void AnAsynchronousCallIsNeverOnThePath()
    {
        // D4.7: it costs the caller nothing, and nothing never wins a maximum. The path through
        // resilient-async goes down the cache and not down the queue, even though the queue leads
        // to a chain that ends at the same database.
        var graph = ReferenceArchitecture.Graph("resilient-async");

        var path = CallCost.Of(graph, CostInputs.Nominal).CriticalPathFrom(graph.RootNodes);

        Assert.DoesNotContain(graph.EdgeId("Api1", "Queue"), path.Select(hop => hop.ConnectionId));
    }

    [Fact]
    public void TheEdgeThatClosesACycleIsNeverOnThePath()
    {
        // D5.4: cut at cost 0, so it cannot win either - which is what makes the reconstruction
        // finite by construction. worst-case has Orders -> Payments -> Inventory -> Orders.
        var graph = ReferenceArchitecture.Graph("worst-case");

        var path = CallCost.Of(graph, CostInputs.Nominal).CriticalPathFrom(graph.RootNodes);

        Assert.DoesNotContain(graph.EdgeId("Inventory", "Orders"), path.Select(hop => hop.ConnectionId));
        Assert.Equal(path.Select(hop => hop.NodeId).Distinct(), path.Select(hop => hop.NodeId));
    }

    [Fact]
    public void ThePathStopsAtTheCallThatFailed()
    {
        // "A failed call costs its timeout, not the hop plus the subtree": the work behind it does
        // not get to happen, so there is no subtree to walk into and the database's 20 ms are not
        // on the bar.
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
        // D4.4 read backwards: resilient-async gets *faster* when the database dies, 39 ms to 17,
        // because the breaker returns at once. A bar of 0 ms would be a bar about nothing, so the
        // path simply ends at the cache - which is also where the story ends.
        var graph = ReferenceArchitecture.Graph("resilient-async");

        var path = CallCost.Of(graph, WithStates((graph.NodeId("Db"), HealthState.Down)))
            .CriticalPathFrom(graph.RootNodes);

        Assert.Equal(["LB", "Api1", "Cache"], path.Select(hop => graph.NodesById[hop.NodeId].Name));
        Assert.Equal(17, path[^1].CumulativeMs);
    }

    [Fact]
    public void ThereIsNoPathWhenThereIsNoDoorLeftStanding()
    {
        // The same condition that makes FaultedLatencyMs null: no path to measure is a different
        // statement from a path that costs zero (plan técnico §2), and an empty list says the first.
        var graph = ReferenceArchitecture.Graph("minimal-api-db");

        var path = CallCost.Of(graph, CostInputs.Nominal).CriticalPathFrom([]);

        Assert.Empty(path);
    }

    // -----------------------------------------------------------------------------------------
    // Anexo A, decisions 1 and 3: what a failing bar paints, and what it annotates.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void AnExpiredCallPaintsWhatWasChargedAndAnnotatesWhatItWouldHaveCost()
    {
        // Plan técnico §8.5. Eight seconds injected into Orders -> Shipping blow that call's
        // timeout, and the cost is big enough to blow the gateway's too: Gateway -> Orders is
        // charged 5 s × 3 attempts = 15 s, and *would* have cost 20 015 ms. The product's own
        // sentence already quotes the second number - "the call takes 8 s and the timeout is 5 s" -
        // so the bar paints the first and writes the second beside it rather than choosing.
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
        // D4.2 and Anexo A, decision 3: without a timeout the call never returns, the retries never
        // happen, and the 30 s bar has to say which of the two kinds of failure it is - otherwise
        // it is just a bar that flattens the rest of the cascade. A null timeout is that mark.
        var graph = ReferenceArchitecture.Graph("worst-case");

        var path = CallCost.Of(graph, WithStates((graph.NodeId("Payments"), HealthState.Down)))
            .CriticalPathFrom(graph.RootNodes);

        // Orders -> Payments has no timeout, so it waits 30 s, and that blows the gateway's own 5 s.
        var orders = path[^1];
        Assert.Equal(HopOutcome.Expired, orders.Outcome);
        Assert.Equal(LatencyModel.UnboundedWait + 15, orders.RawCallMs);

        var unbounded = CallCost.Of(graph, WithStates((graph.NodeId("Payments"), HealthState.Down)))
            .CriticalPathFrom([graph.NodesById[graph.NodeId("Orders")]]);

        var payments = unbounded[^1];
        Assert.Null(payments.TimeoutMs);
        Assert.Equal(0, payments.Retries);       // D4.3: `retries` without a timeout is `retries: 0`
        Assert.Equal(LatencyModel.UnboundedWait, payments.TotalMs);
    }

    // -----------------------------------------------------------------------------------------
    // The invariants. These are the ones that would fail quietly.
    // -----------------------------------------------------------------------------------------

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

            // The last running total is the figure the metrics report - by construction, not by
            // agreement between two walks.
            Assert.Equal(result.Metrics.FaultedLatencyMs, path[^1].CumulativeMs);

            // And every hop after the first arrives through a call that leaves the previous one.
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
        // The decision behind publishing one path rather than two: with nothing broken the faulted
        // walk and the nominal one agree digit for digit, which is exactly the case latency-budget
        // is graded on - it asserts on nominalLatencyMs under an empty scenario.
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
        // D5.6, the property the whole walk is built on: shuffling the lists has to produce the
        // same answer, byte for byte. A reconstruction that picked its entry point by input order
        // would break it silently, which is why the winner is chosen strictly and over id order.
        var (nodes, connections) = ReferenceArchitecture.Load("worst-case");
        var graph = new ArchitectureGraph(nodes, connections);
        var shuffled = new ArchitectureGraph(
            nodes.AsEnumerable().Reverse().ToList(),
            connections.AsEnumerable().Reverse().ToList());

        Assert.Equal(
            CallCost.Of(graph, CostInputs.Nominal).CriticalPathFrom(graph.RootNodes),
            CallCost.Of(shuffled, CostInputs.Nominal).CriticalPathFrom(shuffled.RootNodes));
    }

    /// <summary>Nothing broken, and then each component of the architecture killed in turn.</summary>
    private static IEnumerable<SimulationScenario> Scenarios(ArchitectureGraph graph)
    {
        yield return SimulationScenario.Empty;

        foreach (var node in graph.Nodes.Where(n => !graph.RootNodes.Contains(n)))
            yield return new SimulationScenario([new Fault(ElementType.Node, node.Id, FaultKinds.Down, null)]);

        foreach (var edge in graph.Connections)
            yield return new SimulationScenario([new Fault(ElementType.Connection, edge.Id, FaultKinds.Down, null)]);
    }
}
