using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation.Scenarios;

public class LoadReferenceScenarioTests
{

    [Fact]
    public void MinimalApiDb_300Rps_TheDatabaseRejectsAThirdAndNothingGoesDown()
    {
        var run = LoadScenario.Run("minimal-api-db", 300);

        Assert.Equal(0.6m, run.Utilization("Api"));
        Assert.Equal(1.5m, run.Utilization("Db"));

        Assert.Equal(HealthState.Degraded, run.Node("Db").State);
        Assert.Equal(HealthState.Healthy, run.Node("Api").State);
        Assert.Equal("sim.node.overloaded", run.Node("Db").Reason.Key());

        Assert.Equal(615, run.Metrics.NominalLatencyMs);

        Assert.Equal(1.5m, run.Node("Db").Utilization);
        Assert.Equal(0.6m, run.Node("Api").Utilization);

        Assert.Equal(0.3333m, run.Metrics.RequestsAffected);
        Assert.Equal(FindingElement.Node(run.Graph.NodeId("Db")), run.Metrics.Bottleneck);
        Assert.Equal(1m, run.Metrics.LoadAmplification);
        Assert.Equal(0m, run.Metrics.BacklogRps);
    }


    [Fact]
    public void GatewayMicroservices_300Rps_TheGatewayRoutesAndTheServiceFansOut()
    {
        var run = LoadScenario.Run("gateway-microservices", 300);

        Assert.Equal(100m, run.LoadRps("Users"));
        Assert.Equal(200m, run.LoadRps("Payments"));
        Assert.Equal(400m, run.LoadRps("Db"));
        Assert.Equal(2m, run.Utilization("Db"));

        Assert.Equal(100m, run.LoadRps("Orders"));

        Assert.Empty(run.NodesIn(HealthState.Down));
        Assert.Equal(["Db"], run.NodesIn(HealthState.Degraded));

        Assert.Equal(863, run.Metrics.NominalLatencyMs);

        Assert.Equal(0.5m, run.Metrics.RequestsAffected);
        Assert.Equal(FindingElement.Node(run.Graph.NodeId("Db")), run.Metrics.Bottleneck);
        Assert.Equal(1.33m, run.Metrics.LoadAmplification);
    }


    [Fact]
    public void ResilientAsync_300Rps_TheDatabaseIsAt180PercentAndNobodyNotices()
    {
        var run = LoadScenario.Run("resilient-async", 300);

        Assert.Equal(150m, run.LoadRps("Api1"));
        Assert.Equal(300m, run.LoadRps("Cache"));
        Assert.Equal(360m, run.LoadRps("Db"));
        Assert.Equal(1.8m, run.Utilization("Db"));

        Assert.Equal(0m, run.Metrics.RequestsAffected);
        Assert.Null(run.Metrics.Bottleneck);

        Assert.Equal(["Db", "Orders"], run.NodesIn(HealthState.Degraded));
        Assert.Empty(run.NodesIn(HealthState.Down));

        Assert.Equal(741, run.Metrics.NominalLatencyMs);
        Assert.Equal(1.2m, run.Metrics.LoadAmplification);

        Assert.Equal(0m, run.Metrics.BacklogRps);
    }


    [Fact]
    public void WorstCase_400Rps_TheArchitectureQuadruplesItsOwnLoadByRetrying()
    {
        var run = LoadScenario.Run("worst-case", 400);

        Assert.Equal(1_120m, run.LoadRps("Orders"));
        Assert.Equal(4_144m, run.LoadRps("Shipping"));
        Assert.Equal(5_264m, run.LoadRps("Notifications"));
        Assert.Equal(8_624m, run.LoadRps("Db"));
        Assert.Equal(43.12m, run.Utilization("Db"));
        Assert.Equal(21.56m, run.Metrics.LoadAmplification);

        Assert.Equal(8, run.Metrics.ComponentsDown);
        Assert.Equal(["Db", "Search"], run.NodesIn(HealthState.Degraded));
        Assert.Equal("sim.node.timeoutExceeded", run.Node("Orders").Reason.Key());

        Assert.Equal(15_006, run.Metrics.NominalLatencyMs);
        Assert.Equal(1m, run.Metrics.RequestsAffected);
    }


    [Theory]
    [InlineData("minimal-api-db", 27)]
    [InlineData("gateway-microservices", 57)]
    [InlineData("resilient-async", 39)]
    [InlineData("worst-case", 72)]
    public void With_no_declared_load_nothing_changes(string fixtureName, int nominalMs)
    {
        var graph = ReferenceArchitecture.Graph(fixtureName);
        var result = new SimulationEngine().Run(graph, SimulationScenario.Empty);

        Assert.Equal(nominalMs, result.Metrics.NominalLatencyMs);
        Assert.All(result.States, state => Assert.Null(state.Utilization));
        Assert.Equal(0m, result.Metrics.RequestsAffected);
        Assert.Null(result.Metrics.Bottleneck);
        Assert.Equal(1m, result.Metrics.LoadAmplification);
        Assert.Equal(0m, result.Metrics.BacklogRps);
        Assert.All(graph.Nodes, node => Assert.Equal(1m, LoadModel.Of(graph).QueueingFactorOf(node.Id)));
    }
}
