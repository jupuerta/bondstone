using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation;

/// <summary>
/// V4 bloque DT, one rule at a time, over graphs small enough that the arithmetic above each
/// assertion is the whole argument.
/// </summary>
public class LoadModelTests
{
    [Fact]
    public void With_nothing_declared_every_factor_is_one()
    {
        // The guarantee the whole slice rests on (DT.1): absent load is zero, and zero is the
        // identity. It is why V3's five reference scenarios survive an entirely new axis.
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", protocol: ProtocolType.Sql)
            .Build();

        var load = LoadModel.Of(graph);

        Assert.True(load.IsIdle);
        Assert.All(graph.Nodes, node => Assert.Equal(1m, load.QueueingFactorOf(node.Id)));
        Assert.Equal(1m, load.LoadAmplification);
    }

    [Fact]
    public void A_fan_out_adds_up_because_all_of_those_calls_happen()
    {
        // Correction 7 of plan técnico §2: the load sums what the latency takes the maximum of.
        var graph = Loaded(300, builder => builder
            .Node("A", NodeType.Service)
            .Node("B", NodeType.Service)
            .Node("Db", NodeType.Database)
            .Connect("Api", "A")
            .Connect("Api", "B")
            .Connect("A", "Db", protocol: ProtocolType.Sql)
            .Connect("B", "Db", protocol: ProtocolType.Sql));

        var load = LoadModel.Of(graph);

        Assert.Equal(300m, load.LoadOf(graph.NodeId("A")));
        Assert.Equal(600m, load.LoadOf(graph.NodeId("Db")));
    }

    [Fact]
    public void A_cache_only_lets_through_what_it_misses()
    {
        // DT.3, and the whole of Challenge #004: put a cache in front and the database's utilisation
        // falls to a fifth without anybody touching the database.
        var graph = Loaded(1_000, builder => builder
            .Node("Cache", NodeType.Cache)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Cache")
            .Connect("Cache", "Db", protocol: ProtocolType.Sql));

        var load = LoadModel.Of(graph);

        Assert.Equal(1_000m, load.LoadOf(graph.NodeId("Cache")));
        Assert.Equal(200m, load.LoadOf(graph.NodeId("Db")));   // 1.000 × (1 − 0,8)
    }

    [Fact]
    public void A_balancer_splits_and_instances_multiply_capacity()
    {
        // DT.4. `instances: 3` is three times the requests before saturating, which is the
        // consequence V3 D3.4 did not have when it retired the field.
        var graph = Loaded(900, builder => builder
            .Node("A", NodeType.Service, """{"instances":3}""")
            .Node("B", NodeType.Service)
            .Connect("Api", "A")
            .Connect("Api", "B"));

        var load = LoadModel.Of(graph);

        // The Api is not a balancer, so both calls happen in full.
        Assert.Equal(900m, load.LoadOf(graph.NodeId("A")));
        Assert.Equal(900m, load.CapacityOf(graph.NodeId("A")));   // 300 × 3
        Assert.Equal(300m, load.CapacityOf(graph.NodeId("B")));
        Assert.Equal(1m, load.UtilizationOf(graph.NodeId("A")));
        Assert.Equal(3m, load.UtilizationOf(graph.NodeId("B")));
    }

    [Fact]
    public void A_rate_limit_rejects_at_the_door_instead_of_letting_it_pile_up()
    {
        // DT.3. V3 retired RateLimit saying "a defence against load, and there is no model of
        // load". Now there is one, and it defends: the cheapest lesson in the model.
        var graph = new GraphBuilder()
            .Node("Gateway", NodeType.Gateway, """{"requestsPerSecond":5000,"rateLimit":400}""")
            .Node("Db", NodeType.Database)
            .Connect("Gateway", "Db", protocol: ProtocolType.Sql)
            .Build();

        var load = LoadModel.Of(graph);

        Assert.Equal(400m, load.LoadOf(graph.NodeId("Gateway")));
        Assert.Equal(400m, load.LoadOf(graph.NodeId("Db")));
    }

    [Fact]
    public void The_limit_caps_what_arrives_from_a_client_too()
    {
        // QA §4. Since QA §3.1 the users' traffic is declared on the Client; a limit that only
        // capped the number typed on the gateway itself let the same number typed on the Client
        // straight through. Admission is on everything that reaches the door.
        var graph = new GraphBuilder()
            .Node("Web", NodeType.Client, """{"requestsPerSecond":5000}""")
            .Node("Gateway", NodeType.Gateway, """{"rateLimit":400}""")
            .Node("Db", NodeType.Database)
            .Connect("Web", "Gateway")
            .Connect("Gateway", "Db", protocol: ProtocolType.Sql)
            .Build();

        var load = LoadModel.Of(graph);

        Assert.Equal(400m, load.LoadOf(graph.NodeId("Gateway")));
        Assert.Equal(400m, load.LoadOf(graph.NodeId("Db")));
        Assert.Equal(1m, load.LoadAmplification);   // measured against what was admitted
    }

    [Fact]
    public void An_api_can_be_the_door_that_limits()
    {
        // QA §4: the same lever on the other thing that can be a front door.
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":1000,"rateLimit":300}""")
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", protocol: ProtocolType.Sql)
            .Build();

        var load = LoadModel.Of(graph);

        Assert.Equal(300m, load.LoadOf(graph.NodeId("Api")));
        Assert.Equal(300m, load.LoadOf(graph.NodeId("Db")));
    }

    [Fact]
    public void The_wire_to_the_telemetry_carries_no_traffic_and_splits_nothing()
    {
        // QA §4, coverage. Instrumentation is not requests: the queue still hands its whole load
        // to its one worker after being wired to the telemetry, and the telemetry receives none.
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":100}""")
            .Node("Cola", NodeType.Queue)
            .Node("Worker", NodeType.Worker)
            .Node("Telemetry", NodeType.Observability)
            .Connect("Api", "Cola", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Cola", "Worker", synchronous: false, protocol: ProtocolType.Amqp)
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
        // DT.3. A *rate* of growth needs no clock, which is exactly why V3 could reject backlog
        // (D3.3) and V4 can accept it without acquiring a time axis.
        var graph = Loaded(1_000, builder => builder
            .Node("Queue", NodeType.Queue)
            .Node("Worker", NodeType.Service)
            .Connect("Api", "Queue", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Queue", "Worker", synchronous: false, protocol: ProtocolType.Amqp));

        var load = LoadModel.Of(graph);

        Assert.Equal(1_000m, load.LoadOf(graph.NodeId("Queue")));
        Assert.Equal(300m, load.LoadOf(graph.NodeId("Worker")));   // its whole capacity
        Assert.Equal(700m, load.BacklogRps);
    }

    [Fact]
    public void The_edge_that_closes_a_cycle_carries_nothing()
    {
        // DT.3 and V3 D5.4: going round adds traffic on every lap and never settles. Cutting
        // underreports the nodes inside the cycle, which is written down rather than hidden - and
        // CYCLE already told the user there is one.
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
        // Correction 4. `retries + 1` applied flat would inflate a perfectly healthy architecture
        // by ×3, and V3 is explicit that the multiplier lives only in the failure branches of
        // CallCost - retries fire on failure, not on schedule.
        var graph = Loaded(100, builder => builder
            .Node("Svc", NodeType.Service)
            .Connect("Api", "Svc", timeoutSeconds: 5, retries: 2));

        Assert.Equal(100m, LoadModel.Of(graph).LoadOf(graph.NodeId("Svc")));
    }

    [Fact]
    public void Retries_charge_over_the_fraction_that_fails()
    {
        // 600 rps into a service that can take 300: half the calls fail, so two retries add
        // 2 × 0,5 on top - ×2 rather than ×3.
        var graph = Loaded(600, builder => builder
            .Node("Svc", NodeType.Service)
            .Connect("Api", "Svc", timeoutSeconds: 5, retries: 2));

        Assert.Equal(1_200m, LoadModel.Of(graph).LoadOf(graph.NodeId("Svc")));
    }

    [Fact]
    public void Without_a_timeout_the_retries_never_happen()
    {
        // V3 D4.2, the guard that makes the timeout the key to everything else: with no timeout the
        // call never returns, so there is nothing to retry. `retries: 2` here is `retries: 0`.
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
        // DT.6 and correction 8. Both branches are ×20 at ρ = 1: a step there would be an artificial
        // cliff at exactly the threshold that gets crossed most.
        Assert.Equal((decimal)expected, CapacityModel.QueueingFactor((decimal)utilization));
    }

    private static ArchitectureGraph Loaded(int rps, Func<GraphBuilder, GraphBuilder> build) =>
        build(new GraphBuilder().Node("Api", NodeType.Api, $$"""{"requestsPerSecond":{{rps}}}""")).Build();
}
