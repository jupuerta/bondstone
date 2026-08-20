using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Simulation;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation.Scenarios;

/// <summary>
/// The five scenarios of plan técnico §8, with their states and metrics pinned down. Five and not
/// four because <c>worst-case</c> is the only fixture that can exercise phase B and amplification
/// separately, and mixing them into one scenario would leave both unverified.
/// </summary>
public class ReferenceScenarioTests
{
    // =========================================================================================
    // §8.1  minimal-api-db · node:Db:down - the smallest case.
    // =========================================================================================

    [Fact]
    public void MinimalApiDb_DbDown_TakesTheApiWithIt()
    {
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("minimal-api-db");
        var run = ReferenceScenario.Run("minimal-api-db", ReferenceScenario.NodeDown(graph, "Db"));

        // A dependency with no alternative takes whoever calls it down, and the timeout turns an
        // indefinite wait into five seconds.
        Assert.Equal(HealthState.Down, run.Node("Db").State);
        Assert.Equal(0, run.Node("Db").PropagationStep);
        Assert.Equal("sim.node.injected", run.Node("Db").Reason.Key());

        Assert.Equal(HealthState.Down, run.Edge("Api", "Db").State);
        Assert.Equal(1, run.Edge("Api", "Db").PropagationStep);

        Assert.Equal(HealthState.Down, run.Node("Api").State);
        Assert.Equal(1, run.Node("Api").PropagationStep);
        Assert.Equal("sim.node.unprotected(target=Db)", run.Node("Api").Reason.Describe());

        Assert.Equal(new SimulationMetrics(
            ComponentsDown: 2,
            ComponentsDegraded: 0,
            ComponentsTotal: 2,
            BlastRadius: 1m,
            EntryPointsAffected: 1,
            EntryPointsTotal: 1,
            NominalLatencyMs: 27,
            FaultedLatencyMs: 5_005,
            RequestAmplification: 1m,

            // V4 DT.7 over a V3 scenario: nothing is declared, so the load axis contributes
            // nothing - but the yield walk still has an answer, because a dead dependency with no
            // alternative serves nothing. 100 %, and the culprit is the database.
            RequestsAffected: 1m,
            Bottleneck: FindingElement.Node(graph.NodeId("Db")),
            BacklogRps: 0m,
            LoadAmplification: 1m), run.Metrics);
    }

    // =========================================================================================
    // §8.2  gateway-microservices · node:Db:down - the cascade.
    // =========================================================================================

    [Fact]
    public void GatewayMicroservices_DbDown_CascadesInTwoSteps()
    {
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("gateway-microservices");
        var run = ReferenceScenario.Run("gateway-microservices", ReferenceScenario.NodeDown(graph, "Db"));

        Assert.Equal(0, run.Node("Db").PropagationStep);

        // Their three calls to Db have a timeout but no alternative.
        Assert.Equal(1, run.Node("Users").PropagationStep);
        Assert.Equal(1, run.Node("Orders").PropagationStep);
        Assert.Equal(1, run.Node("Payments").PropagationStep);

        // And only then does the gateway fall: all three of its dependencies are gone.
        Assert.Equal(2, run.Node("Gateway").PropagationStep);

        Assert.Equal(5, run.Metrics.ComponentsDown);
        Assert.Equal(1m, run.Metrics.BlastRadius);
        Assert.Equal(57, run.Metrics.NominalLatencyMs);

        // The gateway's timeout bounds what the user sees to 5 s, even though Orders is inside
        // waiting 30 s on Payments. Both numbers are true and the panel shows the one at the door.
        Assert.Equal(5_005, run.Metrics.FaultedLatencyMs);
    }

    [Fact]
    public void GatewayMicroservices_DbDown_AmplifiesByOne_AndThatIsTheFinding()
    {
        // Orders -> Payments carries retries: 3 and no other edge retries, so over the nominal
        // graph this would report ×4. Under the fault it is ×1, because that edge has no timeout
        // and without a timeout the retries do not get to happen. This is the correction that
        // motivated computing amplification under the scenario (plan técnico §2).
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("gateway-microservices");
        var run = ReferenceScenario.Run("gateway-microservices", ReferenceScenario.NodeDown(graph, "Db"));

        Assert.Equal(1m, run.Metrics.RequestAmplification);
    }

    // =========================================================================================
    // §8.3  resilient-async · node:Db:down - containment.
    // =========================================================================================

    [Fact]
    public void ResilientAsync_DbDown_TheEntryPointKeepsAnswering()
    {
        // The fixture that demonstrates the whole product: the database falls and the entry point
        // is still serving. It is also where the 96-point score and losing half the system stop
        // contradicting each other - the score says how well it is built, the simulation what
        // happens when something breaks (D1.6).
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("resilient-async");
        var run = ReferenceScenario.Run("resilient-async", ReferenceScenario.NodeDown(graph, "Db"));

        Assert.Equal(["Db"], run.NodesIn(HealthState.Down));
        Assert.Equal(["Cache", "Orders"], run.NodesIn(HealthState.Degraded));
        Assert.Equal(["Api1", "Api2", "LB", "Queue"], run.NodesIn(HealthState.Healthy));

        // The containment border is exactly two components, and it is the amber ring the canvas
        // draws with the mechanism's name on it (D6.2).
        Assert.Equal(["Cache:fallback", "Orders:fallback"], run.Contained());

        Assert.Equal(1, run.Node("Cache").PropagationStep);
        Assert.Equal(1, run.Node("Orders").PropagationStep);
    }

    [Fact]
    public void ResilientAsync_DbDown_MetricsAndTheLatencyThatGoesDown()
    {
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("resilient-async");
        var run = ReferenceScenario.Run("resilient-async", ReferenceScenario.NodeDown(graph, "Db"));

        Assert.Equal(new SimulationMetrics(
            ComponentsDown: 1,
            ComponentsDegraded: 2,
            ComponentsTotal: 7,
            BlastRadius: 0.4286m,             // 3 of 7 - the 43 % of §8.3
            EntryPointsAffected: 0,           // the one cell no other scenario covers
            EntryPointsTotal: 1,
            NominalLatencyMs: 39,
            FaultedLatencyMs: 17,             // failing fast is faster than working
            RequestAmplification: 1m,

            // **The pair that summarises the product.** 43 % of the components are tinted and
            // *nobody outside notices*: the fallback on Cache → Db contains, so the entry point
            // serves every request it gets. blastRadius and requestsAffected say different things
            // and both are true - which is why V4 puts them side by side instead of replacing one
            // with the other (DT.7).
            RequestsAffected: 0m,
            Bottleneck: null,
            BacklogRps: 0m,
            LoadAmplification: 1m), run.Metrics);
    }

    // =========================================================================================
    // §8.4  worst-case · node:Gateway:down - reachability. The scenario that uncovered the hole.
    // =========================================================================================

    [Fact]
    public void WorstCase_GatewayDown_LeavesEverythingBehindItWithoutService()
    {
        // Phase A propagates nothing here - nobody depends on a gateway - and without phase B this
        // came out 1/10 with nine components green, in the very architecture where SpofRule marks
        // the gateway as a critical SPOF. Analyzer and simulator would have contradicted each other
        // in the product's flagship example.
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("worst-case");
        var run = ReferenceScenario.Run("worst-case", ReferenceScenario.NodeDown(graph, "Gateway"));

        Assert.Equal(10, run.Metrics.ComponentsDown);
        Assert.Equal(1m, run.Metrics.BlastRadius);
        Assert.Equal(1, run.Metrics.EntryPointsAffected);

        // Nothing left to measure. Not a 0, which would read as "instant" (plan técnico §2).
        Assert.Equal(72, run.Metrics.NominalLatencyMs);
        Assert.Null(run.Metrics.FaultedLatencyMs);
        Assert.Equal(1m, run.Metrics.RequestAmplification);
    }

    [Fact]
    public void WorstCase_GatewayDown_TheWaveTravelsInwardsInThreeSteps()
    {
        // The step is the depth of the walk, and it is not cosmetic: with a flat step the nine
        // components go red at once and the wave of D6.3 stops being a wave - it is a flash.
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("worst-case");
        var run = ReferenceScenario.Run("worst-case", ReferenceScenario.NodeDown(graph, "Gateway"));

        Assert.Equal(0, run.Node("Gateway").PropagationStep);
        Assert.Equal(1, run.Node("Orders").PropagationStep);

        foreach (var name in new[] { "Payments", "Shipping", "Notifications", "Users", "Billing", "Search", "Db" })
            Assert.Equal(2, run.Node(name).PropagationStep);

        // Inventory is only reachable through Payments, so it comes in one wave later.
        Assert.Equal(3, run.Node("Inventory").PropagationStep);
    }

    [Fact]
    public void WorstCase_GatewayDown_EveryVictimPointsAtTheGateway()
    {
        // D4.9: ten red components only read if the panel can say "9 components unreachable
        // because of 'Gateway'" instead of nine identical lines. That grouping is CausedBy.
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("worst-case");
        var run = ReferenceScenario.Run("worst-case", ReferenceScenario.NodeDown(graph, "Gateway"));

        foreach (var name in new[] { "Orders", "Payments", "Inventory", "Shipping", "Notifications", "Users", "Billing", "Search", "Db" })
        {
            Assert.Equal("Gateway", run.NameOf(run.Node(name).CausedBy!));
            Assert.Equal("sim.node.unreachable", run.Node(name).Reason.Key());
        }

        Assert.Null(run.Node("Gateway").CausedBy);
    }

    // =========================================================================================
    // §8.5  worst-case · connection:Orders→Shipping:latency:8000 - the timeout that blows up.
    // =========================================================================================

    [Fact]
    public void WorstCase_InjectedLatency_TopplesHalfTheArchitectureWithoutKillingAnybody()
    {
        // Not a single component is down: just one call that takes longer than its timeout. It is
        // the lesson of D4.1 and the best argument for why timeouts exist.
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("worst-case");
        var run = ReferenceScenario.Run("worst-case", ReferenceScenario.EdgeLatency(graph, "Orders", "Shipping", 8_000));

        Assert.Equal(1, run.Node("Orders").PropagationStep);
        Assert.Equal(2, run.Node("Gateway").PropagationStep);
        Assert.Equal(2, run.Node("Inventory").PropagationStep);
        Assert.Equal(3, run.Node("Payments").PropagationStep);   // closing the cycle

        Assert.Equal(4, run.Metrics.ComponentsDown);
        Assert.Equal(0.4m, run.Metrics.BlastRadius);
        Assert.Equal(72, run.Metrics.NominalLatencyMs);
        Assert.Equal(15_005, run.Metrics.FaultedLatencyMs);
    }

    [Fact]
    public void WorstCase_InjectedLatency_AmplifiesByTwelve()
    {
        // Gateway -> Orders retries twice (×3) and Orders -> Shipping three times (×4). Exactly the
        // number RETRY_AMPLIFICATION reports in V2 over this same fixture, now with a real fault
        // behind it. This is Post 3.
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("worst-case");
        var run = ReferenceScenario.Run("worst-case", ReferenceScenario.EdgeLatency(graph, "Orders", "Shipping", 8_000));

        Assert.Equal(12m, run.Metrics.RequestAmplification);
    }

    [Fact]
    public void WorstCase_InjectedLatency_LeavesTheSixHealthyComponentsGreen()
    {
        // Phase B does not fire - there is no `down` fault - so the six survivors stay green.
        // Without the rule that only injected faults cut, this scenario would come out entirely
        // red and would teach nothing.
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("worst-case");
        var run = ReferenceScenario.Run("worst-case", ReferenceScenario.EdgeLatency(graph, "Orders", "Shipping", 8_000));

        Assert.Equal(
            ["Billing", "Db", "Notifications", "Search", "Shipping", "Users"],
            run.NodesIn(HealthState.Healthy));
    }

    [Fact]
    public void WorstCase_InjectedLatency_SaysHowLongTheCallTakesAndWhatTheTimeoutIs()
    {
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("worst-case");
        var run = ReferenceScenario.Run("worst-case", ReferenceScenario.EdgeLatency(graph, "Orders", "Shipping", 8_000));

        // "La llamada a 'Shipping' tarda 8 s y el timeout es de 5 s: expira siempre…"
        // The two durations travel in milliseconds; turning 8 052 into "8 s" is the client's job now.
        Assert.Equal("sim.node.timeoutExceeded", run.Node("Orders").Reason.Key());
        Assert.Equal("8052", run.Node("Orders").Reason.Arg("callMs"));
        Assert.Equal("5000", run.Node("Orders").Reason.Arg("timeoutMs"));
    }
}
