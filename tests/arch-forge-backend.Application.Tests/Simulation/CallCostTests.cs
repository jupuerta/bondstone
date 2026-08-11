using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation;

/// <summary>
/// The other half of the block 4 table: what each configuration <em>costs</em>. Plus the four
/// reference architectures in nominal regime, whose numbers were worked out by hand in §5.2 and §8
/// of the plan before this class existed.
/// </summary>
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

    // -----------------------------------------------------------------------------------------
    // Nominal regime: the four fixtures, calculated by hand in plan técnico §8.
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("minimal-api-db", 27)]          // Api 5 + Sql 2 + Db 20
    [InlineData("gateway-microservices", 57)]   // Gateway 5 + Http 5 + Orders 10 + Http 5 + Payments 10 + Sql 2 + Db 20
    [InlineData("resilient-async", 39)]         // LB 1 + Http 5 + Api1 5 + Http 5 + Cache 1 + Sql 2 + Db 20
    [InlineData("worst-case", 72)]              // + Shipping 10 + Http 5 + Notifications 10, the longest branch
    public void NominalCostOfTheFourReferenceArchitectures(string fixtureName, int expected)
    {
        var graph = ReferenceArchitecture.Graph(fixtureName);

        var cost = CallCost.Of(graph, CostInputs.Nominal);

        Assert.Equal(expected, cost.WorstFrom(graph.RootNodes));
    }

    [Fact]
    public void NominalRegime_AmplifiesNothing()
    {
        // worst-case has retries on two edges and RETRY_AMPLIFICATION reports ×12 over the static
        // graph. With nothing broken nobody is retrying, so the simulator says ×1 - the number is
        // computed under the scenario, never over the nominal graph (D4.3).
        var graph = ReferenceArchitecture.Graph("worst-case");

        var cost = CallCost.Of(graph, CostInputs.Nominal);

        Assert.Equal(1m, cost.WorstAmplificationFrom(graph.RootNodes));
    }

    [Fact]
    public void AnAsynchronousEdgeCostsTheCallerNothing()
    {
        // D4.7. In resilient-async the LB's worst path goes through Api1 -> Cache -> Db and not
        // through the queue, even though the queue leads to a chain that ends at the same database.
        var graph = ReferenceArchitecture.Graph("resilient-async");

        var cost = CallCost.Of(graph, CostInputs.Nominal);

        Assert.Equal(0, cost.Of(graph.Connections.Single(c => c.Id == graph.EdgeId("Api1", "Queue"))).LatencyMs);
    }

    // -----------------------------------------------------------------------------------------
    // Under fault: a failed call costs its timeout, not the hop plus the subtree.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void ADeadDependencyCostsItsTimeout_NotTheSubtree()
    {
        var graph = ReferenceArchitecture.Graph("minimal-api-db");

        var cost = CallCost.Of(graph, WithStates((graph.NodeId("Db"), HealthState.Down)));

        // Api 5 + timeout 5 s. The 2 ms hop and the 20 ms of the database do not get to happen.
        Assert.Equal(5_005, cost.WorstFrom(graph.RootNodes));
    }

    [Fact]
    public void WithoutATimeout_ADeadDependencyCostsThirtySeconds()
    {
        // D3.1: Orders -> Payments in worst-case has no timeout. The MISSING_RESILIENCE that V2
        // paints as a warning finally has a consequence with a number on it.
        var graph = ReferenceArchitecture.Graph("worst-case");

        var cost = CallCost.Of(graph, WithStates((graph.NodeId("Payments"), HealthState.Down)));

        Assert.Equal(LatencyModel.UnboundedWait, cost.Of(EdgeOf(graph, "Orders", "Payments")).LatencyMs);
    }

    [Fact]
    public void RetriesMultiplyTheWait()
    {
        // D4.3: timeout 5 s × (3 retries + 1) = 20 s, on Orders -> Shipping.
        var graph = ReferenceArchitecture.Graph("worst-case");

        var cost = CallCost.Of(graph, WithStates((graph.NodeId("Shipping"), HealthState.Down)));

        Assert.Equal(20_000, cost.Of(EdgeOf(graph, "Orders", "Shipping")).LatencyMs);
        Assert.Equal(4m, cost.Of(EdgeOf(graph, "Orders", "Shipping")).Amplification);
    }

    [Fact]
    public void WithoutATimeoutTheRetriesDoNotEvenHappen()
    {
        // D4.3, the counterintuitive bit worth having written down: `retries: 3` without a timeout
        // is `retries: 0`. The answer that would let you retry never arrives.
        var graph = ReferenceArchitecture.Graph("gateway-microservices");

        var cost = CallCost.Of(graph, WithStates((graph.NodeId("Payments"), HealthState.Down)));

        Assert.Equal(1m, cost.Of(EdgeOf(graph, "Orders", "Payments")).Amplification);
    }

    [Fact]
    public void TheCircuitBreakerCutsTheCallAtOnceAndTheAmplificationWithIt()
    {
        // D4.4: it bounds latency, it does not save anybody. And it is the only mechanism that cuts
        // retry amplification at the root, which is what makes it the answer to Post 3.
        var graph = ReferenceArchitecture.Graph("resilient-async");

        var cost = CallCost.Of(graph, WithStates((graph.NodeId("Db"), HealthState.Down)));

        Assert.Equal(0, cost.Of(EdgeOf(graph, "Cache", "Db")).LatencyMs);
        Assert.Equal(1m, cost.Of(EdgeOf(graph, "Cache", "Db")).Amplification);
    }

    [Fact]
    public void FailingFastIsFasterThanWorking()
    {
        // resilient-async goes from 39 ms to 17 ms when the database dies, because the breaker cuts
        // the call instantly and saves the 20 ms of the database. It is real, and it is why
        // `addedLatency` disappeared as a metric: a negative "added latency" would read as an
        // improvement in the middle of an outage (plan técnico §2).
        var graph = ReferenceArchitecture.Graph("resilient-async");

        var cost = CallCost.Of(graph, WithStates((graph.NodeId("Db"), HealthState.Down)));

        Assert.Equal(17, cost.WorstFrom(graph.RootNodes));
    }

    // -----------------------------------------------------------------------------------------
    // Injected latency, and the comparison between two numbers that is the reason timeouts exist.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void InjectedLatencyOverTheTimeout_MakesTheCallExpire()
    {
        // Plan técnico §8.5: the call costs 5 + Shipping 47 + 8 000 = 8 052 ms against a 5 s
        // timeout, so it expires always and costs 5 s × 4 attempts.
        var graph = ReferenceArchitecture.Graph("worst-case");
        var edge = EdgeOf(graph, "Orders", "Shipping");

        var cost = CallCost.Of(graph, WithLatency(FindingElement.Connection(edge.Id), 8_000));

        Assert.True(cost.Of(edge).Expires);
        Assert.Equal(20_000, cost.Of(edge).LatencyMs);
    }

    [Fact]
    public void AnExpiredCallBlowsTheTimeoutOfWhoeverCalledIt()
    {
        // The cascading-timeout lesson, for free: a 5 s timeout at the gateway is worth nothing if
        // what sits underneath takes 20. Gateway -> Orders costs 5 s × 3 attempts = 15 s.
        var graph = ReferenceArchitecture.Graph("worst-case");
        var shipping = EdgeOf(graph, "Orders", "Shipping");

        var cost = CallCost.Of(graph, WithLatency(FindingElement.Connection(shipping.Id), 8_000));

        Assert.True(cost.Of(EdgeOf(graph, "Gateway", "Orders")).Expires);
        Assert.Equal(15_005, cost.WorstFrom(graph.RootNodes));
    }

    [Fact]
    public void InjectedLatencyUnderTheTimeout_JustAddsUp()
    {
        // D4.1: latency changes no state by itself. 27 + 1 000 and nothing expires - minimal-api-db
        // has a 5 s timeout.
        var graph = ReferenceArchitecture.Graph("minimal-api-db");
        var edge = EdgeOf(graph, "Api", "Db");

        var cost = CallCost.Of(graph, WithLatency(FindingElement.Connection(edge.Id), 1_000));

        Assert.False(cost.Of(edge).Expires);
        Assert.Equal(1_027, cost.WorstFrom(graph.RootNodes));
    }

    // -----------------------------------------------------------------------------------------
    // D5.4 - the cycle.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void TheEdgeThatClosesACycleContributesNothing()
    {
        // worst-case has a synchronous cycle Orders -> Payments -> Inventory -> Orders, which
        // CycleRule reports and does not forbid. A naive recursive walk does not terminate on it.
        // The back edge is cut at cost 0: a cycle of synchronous calls has no finite cost in
        // reality, so any number would be invented.
        var graph = ReferenceArchitecture.Graph("worst-case");

        var cost = CallCost.Of(graph, CostInputs.Nominal);

        Assert.True(cost.Of(EdgeOf(graph, "Inventory", "Orders")).CycleCut);
        Assert.Equal(0, cost.Of(EdgeOf(graph, "Inventory", "Orders")).LatencyMs);
        // Inventory 10 and nothing else, because its only call is the one that closes the cycle.
        Assert.Equal(10, cost.Of(graph.NodeId("Inventory")).LatencyMs);
    }

    [Fact]
    public void TheCycleCutIsIndependentOfTheOrderTheGraphWasWrittenIn()
    {
        // D5.4: which edge closes the cycle is a property of the graph, not of the input order -
        // the walk enters through the roots and visits outgoing edges ordered by (target, id).
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
