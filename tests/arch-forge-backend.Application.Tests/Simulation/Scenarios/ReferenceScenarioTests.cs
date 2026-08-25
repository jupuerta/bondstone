using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Simulation;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation.Scenarios;

public class ReferenceScenarioTests
{

    [Fact]
    public void MinimalApiDb_DbDown_TakesTheApiWithIt()
    {
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("minimal-api-db");
        var run = ReferenceScenario.Run("minimal-api-db", ReferenceScenario.NodeDown(graph, "Db"));

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

            RequestsAffected: 1m,
            Bottleneck: FindingElement.Node(graph.NodeId("Db")),
            BacklogRps: 0m,
            LoadAmplification: 1m), run.Metrics);
    }


    [Fact]
    public void GatewayMicroservices_DbDown_CascadesInTwoSteps()
    {
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("gateway-microservices");
        var run = ReferenceScenario.Run("gateway-microservices", ReferenceScenario.NodeDown(graph, "Db"));

        Assert.Equal(0, run.Node("Db").PropagationStep);

        Assert.Equal(1, run.Node("Users").PropagationStep);
        Assert.Equal(1, run.Node("Orders").PropagationStep);
        Assert.Equal(1, run.Node("Payments").PropagationStep);

        Assert.Equal(2, run.Node("Gateway").PropagationStep);

        Assert.Equal(5, run.Metrics.ComponentsDown);
        Assert.Equal(1m, run.Metrics.BlastRadius);
        Assert.Equal(57, run.Metrics.NominalLatencyMs);

        Assert.Equal(5_005, run.Metrics.FaultedLatencyMs);
    }

    [Fact]
    public void GatewayMicroservices_DbDown_AmplifiesByOne_AndThatIsTheFinding()
    {
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("gateway-microservices");
        var run = ReferenceScenario.Run("gateway-microservices", ReferenceScenario.NodeDown(graph, "Db"));

        Assert.Equal(1m, run.Metrics.RequestAmplification);
    }


    [Fact]
    public void ResilientAsync_DbDown_TheEntryPointKeepsAnswering()
    {
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("resilient-async");
        var run = ReferenceScenario.Run("resilient-async", ReferenceScenario.NodeDown(graph, "Db"));

        Assert.Equal(["Db"], run.NodesIn(HealthState.Down));
        Assert.Equal(["Cache", "Orders"], run.NodesIn(HealthState.Degraded));
        Assert.Equal(["Api1", "Api2", "LB", "Queue"], run.NodesIn(HealthState.Healthy));

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
            BlastRadius: 0.4286m,
            EntryPointsAffected: 0,
            EntryPointsTotal: 1,
            NominalLatencyMs: 39,
            FaultedLatencyMs: 17,
            RequestAmplification: 1m,

            RequestsAffected: 0m,
            Bottleneck: null,
            BacklogRps: 0m,
            LoadAmplification: 1m), run.Metrics);
    }


    [Fact]
    public void WorstCase_GatewayDown_LeavesEverythingBehindItWithoutService()
    {
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("worst-case");
        var run = ReferenceScenario.Run("worst-case", ReferenceScenario.NodeDown(graph, "Gateway"));

        Assert.Equal(10, run.Metrics.ComponentsDown);
        Assert.Equal(1m, run.Metrics.BlastRadius);
        Assert.Equal(1, run.Metrics.EntryPointsAffected);

        Assert.Equal(72, run.Metrics.NominalLatencyMs);
        Assert.Null(run.Metrics.FaultedLatencyMs);
        Assert.Equal(1m, run.Metrics.RequestAmplification);
    }

    [Fact]
    public void WorstCase_GatewayDown_TheWaveTravelsInwardsInThreeSteps()
    {
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("worst-case");
        var run = ReferenceScenario.Run("worst-case", ReferenceScenario.NodeDown(graph, "Gateway"));

        Assert.Equal(0, run.Node("Gateway").PropagationStep);
        Assert.Equal(1, run.Node("Orders").PropagationStep);

        foreach (var name in new[] { "Payments", "Shipping", "Notifications", "Users", "Billing", "Search", "Db" })
            Assert.Equal(2, run.Node(name).PropagationStep);

        Assert.Equal(3, run.Node("Inventory").PropagationStep);
    }

    [Fact]
    public void WorstCase_GatewayDown_EveryVictimPointsAtTheGateway()
    {
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("worst-case");
        var run = ReferenceScenario.Run("worst-case", ReferenceScenario.NodeDown(graph, "Gateway"));

        foreach (var name in new[] { "Orders", "Payments", "Inventory", "Shipping", "Notifications", "Users", "Billing", "Search", "Db" })
        {
            Assert.Equal("Gateway", run.NameOf(run.Node(name).CausedBy!));
            Assert.Equal("sim.node.unreachable", run.Node(name).Reason.Key());
        }

        Assert.Null(run.Node("Gateway").CausedBy);
    }


    [Fact]
    public void WorstCase_InjectedLatency_TopplesHalfTheArchitectureWithoutKillingAnybody()
    {
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("worst-case");
        var run = ReferenceScenario.Run("worst-case", ReferenceScenario.EdgeLatency(graph, "Orders", "Shipping", 8_000));

        Assert.Equal(1, run.Node("Orders").PropagationStep);
        Assert.Equal(2, run.Node("Gateway").PropagationStep);
        Assert.Equal(2, run.Node("Inventory").PropagationStep);
        Assert.Equal(3, run.Node("Payments").PropagationStep);

        Assert.Equal(4, run.Metrics.ComponentsDown);
        Assert.Equal(0.4m, run.Metrics.BlastRadius);
        Assert.Equal(72, run.Metrics.NominalLatencyMs);
        Assert.Equal(15_005, run.Metrics.FaultedLatencyMs);
    }

    [Fact]
    public void WorstCase_InjectedLatency_AmplifiesByTwelve()
    {
        var graph = Analysis.Fixtures.ReferenceArchitecture.Graph("worst-case");
        var run = ReferenceScenario.Run("worst-case", ReferenceScenario.EdgeLatency(graph, "Orders", "Shipping", 8_000));

        Assert.Equal(12m, run.Metrics.RequestAmplification);
    }

    [Fact]
    public void WorstCase_InjectedLatency_LeavesTheSixHealthyComponentsGreen()
    {
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

        Assert.Equal("sim.node.timeoutExceeded", run.Node("Orders").Reason.Key());
        Assert.Equal("8052", run.Node("Orders").Reason.Arg("callMs"));
        Assert.Equal("5000", run.Node("Orders").Reason.Arg("timeoutMs"));
    }
}
