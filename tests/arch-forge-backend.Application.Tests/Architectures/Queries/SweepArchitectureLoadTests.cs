using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Architectures.Queries;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Application.Tests.Simulation.Scenarios;
using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Architectures.Queries;

public class SweepArchitectureLoadTests
{
    [Fact]
    public void The_ladder_is_geometric_from_one_to_twenty()
    {
        var ladder = SweepArchitectureLoadQueryHandler.Ladder;

        Assert.Equal(14, ladder.Count);
        Assert.Equal(1m, ladder[0]);
        Assert.Equal(20m, ladder[^1]);

        for (var step = 1; step < ladder.Count; step++)
        {
            var ratio = ladder[step] / ladder[step - 1];
            Assert.InRange(ratio, 1.2m, 1.3m);
        }
    }

    [Fact]
    public void The_curve_has_one_point_per_rung_and_the_rungs_are_the_ladder()
    {
        var curve = SweepArchitectureLoadQueryHandler.Sweep(Chain(100));

        Assert.Equal(SweepArchitectureLoadQueryHandler.Ladder, curve.Points.Select(p => p.Scale).ToList());
    }

    [Theory]
    [InlineData("minimal-api-db")]
    [InlineData("resilient-async")]
    [InlineData("gateway-microservices")]
    [InlineData("worst-case")]
    public void The_first_rung_is_the_load_that_the_analysis_already_publishes(string fixtureName)
    {
        var graph = LoadScenario.Run(fixtureName, 400).Graph;

        var first = SweepArchitectureLoadQueryHandler.Sweep(graph).Points[0];
        var analysis = ReferenceArchitecture.Analyze(graph);

        Assert.Equal(1m, first.Scale);
        Assert.Equal(analysis.Load, first.Components);
    }

    [Theory]
    [InlineData("minimal-api-db")]
    [InlineData("resilient-async")]
    [InlineData("gateway-microservices")]
    [InlineData("worst-case")]
    public void The_first_rung_carries_the_latency_that_the_simulation_already_reports(string fixtureName)
    {
        var graph = LoadScenario.Run(fixtureName, 400).Graph;

        var first = SweepArchitectureLoadQueryHandler.Sweep(graph).Points[0];
        var intact = new SimulationEngine().Run(graph, SimulationScenario.Empty);

        Assert.Equal(intact.Metrics.NominalLatencyMs, first.NominalLatencyMs);
        Assert.Equal(intact.Metrics.BacklogRps, first.BacklogRps);
        Assert.Equal(intact.Metrics.LoadAmplification, first.LoadAmplification);
    }

    [Fact]
    public void The_break_point_is_the_first_rung_where_someone_crosses_the_notch()
    {
        var graph = Chain(100);
        var curve = SweepArchitectureLoadQueryHandler.Sweep(graph);

        var before = curve.Points.Single(p => p.Scale == 1.25m);
        Assert.All(before.Components, c => Assert.True(c.Utilization < CapacityModel.SaturationWarning));

        var breakPoint = curve.BreakPoint;
        Assert.NotNull(breakPoint);
        Assert.Equal(1.6m, breakPoint.Scale);
        Assert.Equal(graph.NodeId("Db"), breakPoint.NodeId);
        Assert.Equal(160m, breakPoint.EnteringRps);
        Assert.Equal(160m, breakPoint.Rps);
        Assert.Equal(0.8m, breakPoint.Utilization);
    }

    [Fact]
    public void Nobody_breaks_when_nobody_gets_close()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":1}""")
            .Node("Cache", NodeType.Cache)
            .Connect("Api", "Cache")
            .Build();

        Assert.Null(SweepArchitectureLoadQueryHandler.Sweep(graph).BreakPoint);
    }

    [Fact]
    public void A_graph_with_nothing_declared_has_no_meters_and_no_break_point()
    {
        var curve = SweepArchitectureLoadQueryHandler.Sweep(new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db")
            .Build());

        Assert.Null(curve.BreakPoint);
        Assert.All(curve.Points, point =>
        {
            Assert.Empty(point.Components);
            Assert.Equal(0m, point.EnteringRps);
        });
    }

    [Fact]
    public void The_limiter_flattens_the_curve_and_the_latency_with_it()
    {
        var curve = SweepArchitectureLoadQueryHandler.Sweep(new GraphBuilder()
            .Node("Gateway", NodeType.Gateway, """{"requestsPerSecond":200,"rateLimit":300}""")
            .Node("Svc", NodeType.Service)
            .Connect("Gateway", "Svc")
            .Build());

        var flat = curve.Points.Where(p => p.Scale >= 1.6m).ToList();

        Assert.All(flat, point => Assert.Equal(300m, point.EnteringRps));
        Assert.Single(flat.Select(point => point.NominalLatencyMs).Distinct());
    }

    [Fact]
    public void The_retries_bend_the_curve_upwards_as_the_target_fills_up()
    {
        var curve = SweepArchitectureLoadQueryHandler.Sweep(new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":100}""")
            .Node("Svc", NodeType.Service)
            .Connect("Api", "Svc", timeoutSeconds: 2, retries: 2)
            .Build());

        var amplification = curve.Points.Select(p => p.LoadAmplification).ToList();

        Assert.Equal(1m, amplification[0]);
        Assert.Equal(2.7m, amplification[^1]);
        Assert.Equal(amplification.OrderBy(a => a), amplification);
    }

    [Fact]
    public void The_queue_pins_its_consumer_and_the_rest_piles_up()
    {
        var curve = SweepArchitectureLoadQueryHandler.Sweep(new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":100}""")
            .Node("Queue", NodeType.Queue)
            .Node("Worker", NodeType.Worker)
            .Connect("Api", "Queue", synchronous: false)
            .Connect("Queue", "Worker", synchronous: false)
            .Build());

        var backlog = curve.Points.Select(p => p.BacklogRps).ToList();

        Assert.Equal(0m, backlog[0]);
        Assert.Equal(1800m, backlog[^1]);
        Assert.Equal(backlog.OrderBy(b => b), backlog);
    }

    [Fact]
    public async Task An_unknown_share_token_answers_nothing()
    {
        var repository = new FakeArchitectureRepository();
        var handler = new SweepArchitectureLoadQueryHandler(repository);

        Assert.Null(await handler.Handle(new SweepArchitectureLoadQuery { ShareToken = "nope" }));
    }

    [Fact]
    public async Task The_handler_sweeps_the_stored_graph()
    {
        var repository = new FakeArchitectureRepository();
        var graph = Chain(100);

        repository.Seed(new Architecture
        {
            Id = Guid.NewGuid(),
            Name = "Arquitectura",
            ShareToken = "share-token",
            EditToken = "edit-token",
            Nodes = graph.Nodes.ToList(),
            Connections = graph.Connections.ToList()
        });

        var handler = new SweepArchitectureLoadQueryHandler(repository);
        var curve = await handler.Handle(new SweepArchitectureLoadQuery { ShareToken = "share-token" });

        Assert.NotNull(curve);
        Assert.Equal(SweepArchitectureLoadQueryHandler.Ladder.Count, curve.Points.Count);
        Assert.Equal(1.6m, curve.BreakPoint!.Scale);
    }

    private static ArchitectureGraph Chain(int rps) => new GraphBuilder()
        .Node("Api", NodeType.Api, $$"""{"requestsPerSecond":{{rps}}}""")
        .Node("Db", NodeType.Database)
        .Connect("Api", "Db")
        .Build();
}
