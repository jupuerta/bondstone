using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Application.Tests.Simulation.Scenarios;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation;

public class LoadDemandScaleTests
{
    [Theory]
    [InlineData("minimal-api-db")]
    [InlineData("resilient-async")]
    [InlineData("gateway-microservices")]
    [InlineData("worst-case")]
    public void The_call_without_the_parameter_is_the_call_with_a_one(string fixtureName)
    {
        AssertIdentical(ReferenceArchitecture.Graph(fixtureName));
        AssertIdentical(LoadScenario.Run(fixtureName, 400).Graph);
    }

    [Fact]
    public void Twice_the_scale_is_twice_the_traffic_everywhere()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":100}""")
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", protocol: ProtocolType.Sql)
            .Build();

        var single = LoadModel.Of(graph);
        var doubled = LoadModel.Of(graph, 2);

        Assert.Equal(100m, single.LoadOf(graph.NodeId("Db")));
        Assert.Equal(200m, doubled.LoadOf(graph.NodeId("Db")));
        Assert.Equal(200m, doubled.EnteringRps);
        Assert.Equal(1m, doubled.UtilizationOf(graph.NodeId("Db")));
    }

    [Fact]
    public void A_capacity_that_nobody_declared_stays_idle_at_any_scale()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", protocol: ProtocolType.Sql)
            .Build();

        Assert.True(LoadModel.Of(graph, 20).IsIdle);
        Assert.Equal(0m, LoadModel.Of(graph, 20).EnteringRps);
    }

    [Fact]
    public void The_limiter_flattens_the_curve_and_everything_behind_it_stops_growing()
    {
        var graph = new GraphBuilder()
            .Node("Gateway", NodeType.Gateway, """{"requestsPerSecond":200,"rateLimit":300}""")
            .Node("Svc", NodeType.Service)
            .Connect("Gateway", "Svc")
            .Build();

        var svc = graph.NodeId("Svc");

        Assert.Equal(200m, LoadModel.Of(graph).LoadOf(svc));
        Assert.Equal(250m, LoadModel.Of(graph, 1.25m).LoadOf(svc));

        Assert.Equal(300m, LoadModel.Of(graph, 1.6m).LoadOf(svc));
        Assert.Equal(300m, LoadModel.Of(graph, 20).LoadOf(svc));
        Assert.Equal(300m, LoadModel.Of(graph, 20).EnteringRps);
    }

    [Fact]
    public void The_retries_amplify_more_as_the_target_fills_up()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":100}""")
            .Node("Svc", NodeType.Service)
            .Connect("Api", "Svc", timeoutSeconds: 2, retries: 2)
            .Build();

        var svc = graph.NodeId("Svc");

        Assert.Equal(100m, LoadModel.Of(graph).LoadOf(svc));
        Assert.Equal(200m, LoadModel.Of(graph, 2).LoadOf(svc));

        Assert.Equal(345m, Math.Round(LoadModel.Of(graph, 3.15m).LoadOf(svc), 2));
        Assert.Equal(600m, LoadModel.Of(graph, 4).LoadOf(svc));

        Assert.Equal(1m, LoadModel.Of(graph, 2).LoadAmplification);
        Assert.Equal(1.5m, LoadModel.Of(graph, 4).LoadAmplification);
        Assert.Equal(2.7m, LoadModel.Of(graph, 20).LoadAmplification);
    }

    [Fact]
    public void What_the_consumer_cannot_chew_becomes_backlog_when_the_scale_goes_up()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":100}""")
            .Node("Queue", NodeType.Queue)
            .Node("Worker", NodeType.Worker)
            .Connect("Api", "Queue", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Queue", "Worker", synchronous: false, protocol: ProtocolType.Amqp)
            .Build();

        var worker = graph.NodeId("Worker");

        Assert.Equal(0m, LoadModel.Of(graph, 2).BacklogRps);
        Assert.Equal(200m, LoadModel.Of(graph, 2).LoadOf(worker));

        Assert.Equal(200m, LoadModel.Of(graph, 4).LoadOf(worker));
        Assert.Equal(200m, LoadModel.Of(graph, 4).BacklogRps);
    }

    private static void AssertIdentical(ArchitectureGraph graph)
    {
        var today = LoadModel.Of(graph);
        var one = LoadModel.Of(graph, 1);

        Assert.Equal(today.IsIdle, one.IsIdle);
        Assert.Equal(today.BacklogRps, one.BacklogRps);
        Assert.Equal(today.EnteringRps, one.EnteringRps);
        Assert.Equal(today.LoadAmplification, one.LoadAmplification);

        foreach (var node in graph.Nodes)
        {
            Assert.Equal(today.DemandOf(node.Id), one.DemandOf(node.Id));
            Assert.Equal(today.LoadOf(node.Id), one.LoadOf(node.Id));
            Assert.Equal(today.CapacityOf(node.Id), one.CapacityOf(node.Id));
            Assert.Equal(today.UtilizationOf(node.Id), one.UtilizationOf(node.Id));
            Assert.Equal(today.QueueingFactorOf(node.Id), one.QueueingFactorOf(node.Id));
        }

        foreach (var edge in graph.Connections)
            Assert.Equal(today.CallLoadOf(edge.Id), one.CallLoadOf(edge.Id));
    }
}
