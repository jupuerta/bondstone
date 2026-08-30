using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation;

public class LoadModelTests
{
    [Fact]
    public void With_nothing_declared_every_factor_is_one()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db")
            .Build();

        var load = LoadModel.Of(graph);

        Assert.True(load.IsIdle);
        Assert.All(graph.Nodes, node => Assert.Equal(1m, load.QueueingFactorOf(node.Id)));
        Assert.Equal(1m, load.LoadAmplification);
    }

    [Fact]
    public void A_fan_out_adds_up_because_all_of_those_calls_happen()
    {
        var graph = Loaded(300, builder => builder
            .Node("A", NodeType.Service)
            .Node("B", NodeType.Service)
            .Node("Db", NodeType.Database)
            .Connect("Api", "A")
            .Connect("Api", "B")
            .Connect("A", "Db")
            .Connect("B", "Db"));

        var load = LoadModel.Of(graph);

        Assert.Equal(300m, load.LoadOf(graph.NodeId("A")));
        Assert.Equal(600m, load.LoadOf(graph.NodeId("Db")));
    }

    [Fact]
    public void A_cache_only_lets_through_what_it_misses()
    {
        var graph = Loaded(1_000, builder => builder
            .Node("Cache", NodeType.Cache)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Cache")
            .Connect("Cache", "Db"));

        var load = LoadModel.Of(graph);

        Assert.Equal(1_000m, load.LoadOf(graph.NodeId("Cache")));
        Assert.Equal(200m, load.LoadOf(graph.NodeId("Db")));
    }

    [Fact]
    public void A_balancer_splits_and_instances_multiply_capacity()
    {
        var graph = Loaded(900, builder => builder
            .Node("A", NodeType.Service, """{"instances":3}""")
            .Node("B", NodeType.Service)
            .Connect("Api", "A")
            .Connect("Api", "B"));

        var load = LoadModel.Of(graph);

        Assert.Equal(900m, load.LoadOf(graph.NodeId("A")));
        Assert.Equal(900m, load.CapacityOf(graph.NodeId("A")));
        Assert.Equal(300m, load.CapacityOf(graph.NodeId("B")));
        Assert.Equal(1m, load.UtilizationOf(graph.NodeId("A")));
        Assert.Equal(3m, load.UtilizationOf(graph.NodeId("B")));
    }

    [Fact]
    public void A_gateway_routes_a_request_down_one_route_instead_of_all_of_them()
    {
        var graph = new GraphBuilder()
            .Node("Web", NodeType.Client, """{"requestsPerSecond":600}""")
            .Node("Gateway", NodeType.Gateway)
            .Nodes(NodeType.Service, "A", "B", "C")
            .Connect("Web", "Gateway")
            .Connect("Gateway", "A")
            .Connect("Gateway", "B")
            .Connect("Gateway", "C")
            .Build();

        var load = LoadModel.Of(graph);

        Assert.Equal(600m, load.LoadOf(graph.NodeId("Gateway")));
        Assert.All(["A", "B", "C"], name => Assert.Equal(200m, load.LoadOf(graph.NodeId(name))));
        Assert.All(["A", "B", "C"], name => Assert.True(load.UtilizationOf(graph.NodeId(name)) < 1m));
    }

    [Fact]
    public void A_second_gateway_for_redundancy_does_not_double_the_database()
    {
        var single = Fronted("Gw1");
        var redundant = Fronted("Gw1", "Gw2");

        Assert.Equal(150m, LoadModel.Of(single).LoadOf(single.NodeId("SvcA")));
        Assert.Equal(300m, LoadModel.Of(single).LoadOf(single.NodeId("Db")));

        Assert.Equal(150m, LoadModel.Of(redundant).LoadOf(redundant.NodeId("SvcA")));
        Assert.Equal(300m, LoadModel.Of(redundant).LoadOf(redundant.NodeId("Db")));
    }

    private static ArchitectureGraph Fronted(params string[] gateways)
    {
        var builder = new GraphBuilder()
            .Node("Web", NodeType.Client, """{"requestsPerSecond":300}""")
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Service, "SvcA", "SvcB")
            .Node("Db", NodeType.Database)
            .Connect("Web", "LB")
            .Connect("SvcA", "Db")
            .Connect("SvcB", "Db");

        foreach (var gateway in gateways)
        {
            builder.Node(gateway, NodeType.Gateway)
                .Connect("LB", gateway)
                .Connect(gateway, "SvcA")
                .Connect(gateway, "SvcB");
        }

        return builder.Build();
    }

    [Fact]
    public void A_rate_limit_rejects_at_the_door_instead_of_letting_it_pile_up()
    {
        var graph = new GraphBuilder()
            .Node("Gateway", NodeType.Gateway, """{"requestsPerSecond":5000,"rateLimit":400}""")
            .Node("Db", NodeType.Database)
            .Connect("Gateway", "Db")
            .Build();

        var load = LoadModel.Of(graph);

        Assert.Equal(400m, load.LoadOf(graph.NodeId("Gateway")));
        Assert.Equal(400m, load.LoadOf(graph.NodeId("Db")));
    }

    [Fact]
    public void The_limit_caps_what_arrives_from_a_client_too()
    {
        var graph = new GraphBuilder()
            .Node("Web", NodeType.Client, """{"requestsPerSecond":5000}""")
            .Node("Gateway", NodeType.Gateway, """{"rateLimit":400}""")
            .Node("Db", NodeType.Database)
            .Connect("Web", "Gateway")
            .Connect("Gateway", "Db")
            .Build();

        var load = LoadModel.Of(graph);

        Assert.Equal(400m, load.LoadOf(graph.NodeId("Gateway")));
        Assert.Equal(400m, load.LoadOf(graph.NodeId("Db")));
        Assert.Equal(1m, load.LoadAmplification);
    }

    [Fact]
    public void An_api_can_be_the_door_that_limits()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":1000,"rateLimit":300}""")
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db")
            .Build();

        var load = LoadModel.Of(graph);

        Assert.Equal(300m, load.LoadOf(graph.NodeId("Api")));
        Assert.Equal(300m, load.LoadOf(graph.NodeId("Db")));
    }

    [Fact]
    public void The_wire_to_the_telemetry_carries_no_traffic_and_splits_nothing()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":100}""")
            .Node("Cola", NodeType.Queue)
            .Node("Worker", NodeType.Worker)
            .Node("Telemetry", NodeType.Observability)
            .Connect("Api", "Cola", synchronous: false)
            .Connect("Cola", "Worker", synchronous: false)
            .Connect("Cola", "Telemetry", synchronous: false)
            .Connect("Api", "Telemetry", synchronous: false)
            .Build();

        var load = LoadModel.Of(graph);

        Assert.Equal(100m, load.LoadOf(graph.NodeId("Worker")));
        Assert.Equal(0m, load.LoadOf(graph.NodeId("Telemetry")));
    }

    [Fact]
    public void A_queue_delivers_what_its_consumer_can_chew_and_the_rest_is_backlog()
    {
        var graph = Loaded(1_000, builder => builder
            .Node("Queue", NodeType.Queue)
            .Node("Worker", NodeType.Service)
            .Connect("Api", "Queue", synchronous: false)
            .Connect("Queue", "Worker", synchronous: false));

        var load = LoadModel.Of(graph);

        Assert.Equal(1_000m, load.LoadOf(graph.NodeId("Queue")));
        Assert.Equal(300m, load.LoadOf(graph.NodeId("Worker")));
        Assert.Equal(700m, load.BacklogRps);
    }

    [Fact]
    public void The_edge_that_closes_a_cycle_carries_nothing()
    {
        var graph = Loaded(100, builder => builder
            .Node("A", NodeType.Service)
            .Node("B", NodeType.Service)
            .Connect("Api", "A")
            .Connect("A", "B")
            .Connect("B", "A"));

        var load = LoadModel.Of(graph);

        Assert.Equal(100m, load.LoadOf(graph.NodeId("A")));
        Assert.Equal(100m, load.LoadOf(graph.NodeId("B")));
    }

    [Fact]
    public void Retries_do_not_charge_while_everything_answers()
    {
        var graph = Loaded(100, builder => builder
            .Node("Svc", NodeType.Service)
            .Connect("Api", "Svc", timeoutSeconds: 5, retries: 2));

        Assert.Equal(100m, LoadModel.Of(graph).LoadOf(graph.NodeId("Svc")));
    }

    [Fact]
    public void Retries_charge_over_the_fraction_that_fails()
    {
        var graph = Loaded(600, builder => builder
            .Node("Svc", NodeType.Service)
            .Connect("Api", "Svc", timeoutSeconds: 5, retries: 2));

        Assert.Equal(1_200m, LoadModel.Of(graph).LoadOf(graph.NodeId("Svc")));
    }

    [Fact]
    public void Without_a_timeout_the_retries_never_happen()
    {
        var graph = Loaded(600, builder => builder
            .Node("Svc", NodeType.Service)
            .Connect("Api", "Svc", retries: 2));

        Assert.Equal(600m, LoadModel.Of(graph).LoadOf(graph.NodeId("Svc")));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0.5, 2)]
    [InlineData(0.9, 10)]
    [InlineData(1, 20)]
    [InlineData(2, 40)]
    [InlineData(6, 120)]
    public void The_queueing_factor_is_continuous_where_the_two_branches_meet(double utilization, double expected)
    {
        Assert.Equal((decimal)expected, CapacityModel.QueueingFactor((decimal)utilization));
    }

    private static ArchitectureGraph Loaded(int rps, Func<GraphBuilder, GraphBuilder> build) =>
        build(new GraphBuilder().Node("Api", NodeType.Api, $$"""{"requestsPerSecond":{{rps}}}""")).Build();
}
