using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation.Scenarios;

/// <summary>
/// V4 plan técnico §8, the four load scenarios. One per fixture, and between them they tell four
/// different stories - nuisance · a database everybody shares · a fault absorbed · collapse - which
/// is what the eighth correction was for. Before the queueing factor was allowed to keep growing
/// past capacity, all four said "everything amber" and the database contributed the same 400 ms to
/// every one of them.
/// </summary>
public class LoadReferenceScenarioTests
{
    // =============================================================================================
    // §8.1  minimal-api-db · 300 rps - the smallest case, and the one that pins correction 1.
    // =============================================================================================

    [Fact]
    public void MinimalApiDb_300Rps_TheDatabaseRejectsAThirdAndNothingGoesDown()
    {
        var run = LoadScenario.Run("minimal-api-db", 300);

        // Api 300/500 · Db 300/200.
        Assert.Equal(0.6m, run.Utilization("Api"));
        Assert.Equal(1.5m, run.Utilization("Db"));

        // **Correction 1.** The database is at 150 % and it is Degraded, never Down. With a Down
        // here, the Api - which calls without a fallback - would inherit it and the whole
        // architecture would be red, turning "it rejects a third of the requests" into "everything
        // is dead" (plan técnico §2).
        Assert.Equal(HealthState.Degraded, run.Node("Db").State);
        Assert.Equal(HealthState.Healthy, run.Node("Api").State);
        Assert.Equal("sim.node.overloaded", run.Node("Db").Reason.Key());

        // Api 5 × 2,5 = 13 · Db 20 × 30 = 600 · 13 + Sql 2 + 600.
        // Without any declared load this same fixture costs 27 ms, and V3's scenario still says so.
        Assert.Equal(615, run.Metrics.NominalLatencyMs);

        // DT.5: the figure travels on the element itself, so the canvas can print "150 %" on the
        // box instead of making the user open a panel to find out what the amber means.
        Assert.Equal(1.5m, run.Node("Db").Utilization);
        Assert.Equal(0.6m, run.Node("Api").Utilization);

        Assert.Equal(0.3333m, run.Metrics.RequestsAffected);
        Assert.Equal(FindingElement.Node(run.Graph.NodeId("Db")), run.Metrics.Bottleneck);
        Assert.Equal(1m, run.Metrics.LoadAmplification);
        Assert.Equal(0m, run.Metrics.BacklogRps);
    }

    // =============================================================================================
    // §8.2  gateway-microservices · 300 rps - routing, fan-out, and the database underneath both.
    // =============================================================================================

    [Fact]
    public void GatewayMicroservices_300Rps_TheGatewayRoutesAndTheServiceFansOut()
    {
        var run = LoadScenario.Run("gateway-microservices", 300);

        // **QA V14 B5, and the two behaviours the scenario now separates.** A gateway *routes*: each
        // request goes down one of its three routes, so 300 rps at the door are 100 rps per service
        // and not 300 each. A service's fan-out is something else entirely - Orders makes two calls
        // per request it serves, so both of them carry its full 100 rps. Until V14 the gateway
        // duplicated like a service, splitting a monolith into three relieved nothing in the model,
        // and 300 real requests were charged as 900.
        Assert.Equal(100m, run.LoadRps("Users"));
        Assert.Equal(200m, run.LoadRps("Payments"));   // 100 routed by the gateway + 100 from Orders
        Assert.Equal(400m, run.LoadRps("Db"));         // 100 + 100 + 200
        Assert.Equal(2m, run.Utilization("Db"));

        // Correction 7 still holds where it applies: Orders takes 100 rps and hands 100 to *each* of
        // its two dependencies, because both calls happen - while the latency takes the maximum of
        // the same two, because they are parallel. Each axis reads the same fan-out with the
        // question that is its own.
        Assert.Equal(100m, run.LoadRps("Orders"));

        // Nobody is down and nothing has failed, and the three services are comfortable. Splitting
        // the traffic three ways bought all of them room and bought the database nothing - it is the
        // only box past its capacity, and 863 ms and half the requests lost are all its doing.
        Assert.Empty(run.NodesIn(HealthState.Down));
        Assert.Equal(["Db"], run.NodesIn(HealthState.Degraded));

        // Gateway 6 + Http 5 + Orders 15 + Http 5 + Payments 30 + Sql 2 + Db 800.
        Assert.Equal(863, run.Metrics.NominalLatencyMs);

        // The bound of correction 2, naming the one component that sets it. With the gateway routing
        // there is a single fraction below 1 here, so this fixture no longer tells the bound apart
        // from the product it replaced - that argument lives in RequestYieldTests, where two
        // bottlenecks in series give 50 % and not 25 %.
        Assert.Equal(0.5m, run.Metrics.RequestsAffected);
        Assert.Equal(FindingElement.Node(run.Graph.NodeId("Db")), run.Metrics.Bottleneck);
        Assert.Equal(1.33m, run.Metrics.LoadAmplification);
    }

    // =============================================================================================
    // §8.3  resilient-async · 300 rps - containment under load, and correction 3.
    // =============================================================================================

    [Fact]
    public void ResilientAsync_300Rps_TheDatabaseIsAt180PercentAndNobodyNotices()
    {
        var run = LoadScenario.Run("resilient-async", 300);

        // The balancer splits, and the cache does its job: 300 rps reach it and 60 get through.
        Assert.Equal(150m, run.LoadRps("Api1"));
        Assert.Equal(300m, run.LoadRps("Cache"));
        Assert.Equal(360m, run.LoadRps("Db"));        // 60 through the cache + 300 through Orders
        Assert.Equal(1.8m, run.Utilization("Db"));

        // **The headline of V4.** The database is at 180 % and the entry point serves every request
        // it gets, because Api → Cache carries a timeout and a fallback.
        //
        // **Correction 3** is what makes this true: there is no injected fault here, so phase A
        // never runs and containment had to be read from the edge itself, in RequestYield.
        Assert.Equal(0m, run.Metrics.RequestsAffected);
        Assert.Null(run.Metrics.Bottleneck);

        Assert.Equal(["Db", "Orders"], run.NodesIn(HealthState.Degraded));
        Assert.Empty(run.NodesIn(HealthState.Down));

        // LB 1 + Http 5 + Api1 7 + Http 5 + Cache 1 + Sql 2 + Db 720.
        Assert.Equal(741, run.Metrics.NominalLatencyMs);
        Assert.Equal(1.2m, run.Metrics.LoadAmplification);

        // The consumer sits exactly at capacity, so nothing piles up - yet.
        Assert.Equal(0m, run.Metrics.BacklogRps);
    }

    // =============================================================================================
    // §8.4  worst-case · 400 rps - the retry storm, and corrections 4, 5 and 8.
    // =============================================================================================

    [Fact]
    public void WorstCase_400Rps_TheArchitectureQuadruplesItsOwnLoadByRetrying()
    {
        var run = LoadScenario.Run("worst-case", 400);

        // **Corrections 4 and 5.** Pass 1 carries no amplification and the database receives 2.000
        // rps. Pass 2 charges the retries over the fraction that fails, and it receives **8.624**.
        // Same architecture, same traffic at the door, and **four times the work**, purely from
        // being configured to retry. This is Post 3 with no fault injected at all.
        //
        // The fraction that fails is the *yield of the whole subtree*, not the target's own
        // capacity: 90 % of the calls into Orders fail because Orders' database is drowning, so
        // Gateway → Orders multiplies by 1 + 2 × 0,9 = 2,8 rather than by the 1,5 its own
        // utilisation would suggest. That is what makes a retry storm a storm - the amplification
        // is driven by how badly things are going *underneath*, not next door.
        Assert.Equal(1_120m, run.LoadRps("Orders"));      // 400 × 2,8
        Assert.Equal(4_144m, run.LoadRps("Shipping"));    // 1.120 × 3,7
        Assert.Equal(5_264m, run.LoadRps("Notifications"));
        Assert.Equal(8_624m, run.LoadRps("Db"));
        Assert.Equal(43.12m, run.Utilization("Db"));
        Assert.Equal(21.56m, run.Metrics.LoadAmplification);

        // **Correction 8, and the reason the four scenarios stopped being interchangeable.** The
        // database at 1.725 % costs 6,9 s, a 6,9 s call against a 5 s timeout expires, and the
        // caller falls through TimeoutExceeded - V3's path, with EdgeEffect untouched. Saturation
        // killed nobody directly and eight components are down.
        Assert.Equal(8, run.Metrics.ComponentsDown);
        Assert.Equal(["Db", "Search"], run.NodesIn(HealthState.Degraded));
        Assert.Equal("sim.node.timeoutExceeded", run.Node("Orders").Reason.Key());

        // Orders → Shipping expires: 5 s × 4 = 20.000. Gateway → Orders expires: 5 s × 3 = 15.000.
        Assert.Equal(15_006, run.Metrics.NominalLatencyMs);
        Assert.Equal(1m, run.Metrics.RequestsAffected);
    }

    // =============================================================================================
    // The guarantee the whole slice rests on.
    // =============================================================================================

    [Theory]
    [InlineData("minimal-api-db", 27)]
    [InlineData("gateway-microservices", 57)]
    [InlineData("resilient-async", 39)]
    [InlineData("worst-case", 72)]
    public void With_no_declared_load_nothing_changes(string fixtureName, int nominalMs)
    {
        // DT.1 and DT.6, and the first box of the definition of done: absent load is zero,
        // utilisation is zero, the queueing factor is exactly 1, and V4's whole new axis is the
        // identity. Every architecture drawn before V4 behaves as it did, to the digit.
        var graph = ReferenceArchitecture.Graph(fixtureName);
        var result = new SimulationEngine().Run(graph, SimulationScenario.Empty);

        Assert.Equal(nominalMs, result.Metrics.NominalLatencyMs);
        // No declared traffic means no utilisation at all - null, not zero: "nobody measured this"
        // is a different statement from "it is idle".
        Assert.All(result.States, state => Assert.Null(state.Utilization));
        Assert.Equal(0m, result.Metrics.RequestsAffected);
        Assert.Null(result.Metrics.Bottleneck);
        Assert.Equal(1m, result.Metrics.LoadAmplification);
        Assert.Equal(0m, result.Metrics.BacklogRps);
        Assert.All(graph.Nodes, node => Assert.Equal(1m, LoadModel.Of(graph).QueueingFactorOf(node.Id)));
    }
}
