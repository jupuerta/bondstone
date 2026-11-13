using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Architectures.Queries;
using bondstone_backend.Application.Simulation;
using bondstone_backend.Application.Tests.Analysis.Fixtures;
using bondstone_backend.Application.Tests.Analysis.Rules.Support;
using bondstone_backend.Application.Tests.TestDoubles;
using bondstone_backend.Domain.Entities;
using bondstone_backend.Domain.Enums;
using Xunit;

namespace bondstone_backend.Application.Tests.Architectures.Queries;

public class MapArchitectureFragilityTests
{
    private static readonly SimulationEngine Engine = new();

    [Theory]
    [InlineData("minimal-api-db")]
    [InlineData("resilient-async")]
    [InlineData("gateway-microservices")]
    [InlineData("worst-case")]
    public void There_is_one_row_per_component_and_nothing_else(string fixtureName)
    {
        var graph = ReferenceArchitecture.Graph(fixtureName);
        var map = MapArchitectureFragilityQueryHandler.Sweep(graph, Engine);

        Assert.Equal(graph.Nodes.Count, map.ComponentsTotal);
        Assert.Equal(graph.EntryNodes.Count, map.EntryPointsTotal);
        Assert.Equal(graph.Nodes.Select(n => n.Id).OrderBy(id => id), map.Rows.Select(r => r.NodeId).OrderBy(id => id));
    }

    [Theory]
    [InlineData("minimal-api-db")]
    [InlineData("resilient-async")]
    [InlineData("gateway-microservices")]
    [InlineData("worst-case")]
    public void Every_row_is_the_simulation_that_the_user_could_have_injected_by_hand(string fixtureName)
    {
        var graph = ReferenceArchitecture.Graph(fixtureName);
        var map = MapArchitectureFragilityQueryHandler.Sweep(graph, Engine);

        foreach (var row in map.Rows)
        {
            var byHand = Engine.Run(graph, new SimulationScenario([new Fault(ElementType.Node, row.NodeId, FaultKinds.Down)]));
            var metrics = byHand.Metrics;

            Assert.Equal(metrics.ComponentsDown, row.ComponentsDown);
            Assert.Equal(metrics.ComponentsDegraded, row.ComponentsDegraded);
            Assert.Equal(metrics.BlastRadius, row.BlastRadius);
            Assert.Equal(metrics.EntryPointsAffected, row.EntryPointsAffected);
            Assert.Equal(metrics.RequestsAffected, row.RequestsAffected);
            Assert.Equal(metrics.FaultedLatencyMs, row.FaultedLatencyMs);

            Assert.Equal(Elements(byHand, HealthState.Down), row.Down);
            Assert.Equal(Elements(byHand, HealthState.Degraded), row.Degraded);
        }
    }

    [Theory]
    [InlineData("minimal-api-db")]
    [InlineData("resilient-async")]
    [InlineData("gateway-microservices")]
    [InlineData("worst-case")]
    public void The_nominal_latency_is_the_one_of_the_architecture_intact(string fixtureName)
    {
        var graph = ReferenceArchitecture.Graph(fixtureName);

        var map = MapArchitectureFragilityQueryHandler.Sweep(graph, Engine);
        var intact = Engine.Run(graph, SimulationScenario.Empty);

        Assert.Equal(intact.Metrics.NominalLatencyMs, map.NominalLatencyMs);
    }

    [Theory]
    [InlineData("minimal-api-db")]
    [InlineData("resilient-async")]
    [InlineData("gateway-microservices")]
    [InlineData("worst-case")]
    public void The_rows_come_ordered_by_blast_radius_with_the_id_breaking_the_ties(string fixtureName)
    {
        var rows = MapArchitectureFragilityQueryHandler
            .Sweep(ReferenceArchitecture.Graph(fixtureName), Engine)
            .Rows;

        var expected = rows.OrderByDescending(r => r.BlastRadius).ThenBy(r => r.NodeId).ToList();

        Assert.Equal(expected, rows);
    }

    [Fact]
    public void The_gateway_that_takes_everything_with_it_leads_the_worst_case()
    {
        var graph = ReferenceArchitecture.Graph("worst-case");
        var map = MapArchitectureFragilityQueryHandler.Sweep(graph, Engine);

        var first = map.Rows[0];

        Assert.Equal(10, map.ComponentsTotal);
        Assert.Equal(10, first.ComponentsDown);
        Assert.Equal(1m, first.BlastRadius);
        Assert.Equal(1m, first.RequestsAffected);
        Assert.Equal(1, first.EntryPointsAffected);
    }

    [Fact]
    public void The_short_bars_are_the_lesson_and_they_are_the_ones_behind_a_mechanism()
    {
        var graph = ReferenceArchitecture.Graph("resilient-async");
        var map = MapArchitectureFragilityQueryHandler.Sweep(graph, Engine);

        var balancer = Row(map, graph, "LB");
        var database = Row(map, graph, "Db");

        Assert.Equal(1m, balancer.BlastRadius);
        Assert.Equal(1m, balancer.RequestsAffected);

        Assert.Equal(0.4286m, database.BlastRadius);
        Assert.Equal(0m, database.RequestsAffected);
        Assert.Equal(0, database.EntryPointsAffected);
    }

    [Fact]
    public void A_row_carries_the_ids_of_what_falls_so_the_canvas_can_paint_it_without_asking_again()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":100}""")
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db")
            .Build();

        var map = MapArchitectureFragilityQueryHandler.Sweep(graph, Engine);
        var row = map.Rows.Single(r => r.NodeId == graph.NodeId("Db"));

        Assert.Equal(
            [
                FindingElement.Node(graph.NodeId("Api")),
                FindingElement.Node(graph.NodeId("Db")),
                FindingElement.Connection(graph.EdgeId("Api", "Db"))
            ],
            row.Down.OrderBy(e => e.Type).ThenBy(e => e.Id));

        Assert.Empty(row.Degraded);
    }

    [Fact]
    public void An_empty_architecture_answers_an_empty_map()
    {
        var map = MapArchitectureFragilityQueryHandler.Sweep(new ArchitectureGraph([], []), Engine);

        Assert.Empty(map.Rows);
        Assert.Equal(0, map.ComponentsTotal);
        Assert.Equal(0, map.NominalLatencyMs);
    }

    [Fact]
    public async Task An_unknown_share_token_answers_nothing()
    {
        var handler = new MapArchitectureFragilityQueryHandler(new FakeArchitectureRepository(), Engine);

        Assert.Null(await handler.Handle(new MapArchitectureFragilityQuery { ShareToken = "nope" }));
    }

    [Fact]
    public async Task The_handler_sweeps_the_stored_graph()
    {
        var repository = new FakeArchitectureRepository();
        var (nodes, connections) = ReferenceArchitecture.Load("gateway-microservices");

        repository.Seed(new Architecture
        {
            Id = Guid.NewGuid(),
            Name = "Arquitectura",
            ShareToken = "share-token",
            EditToken = "edit-token",
            Nodes = nodes,
            Connections = connections
        });

        var handler = new MapArchitectureFragilityQueryHandler(repository, Engine);
        var map = await handler.Handle(new MapArchitectureFragilityQuery { ShareToken = "share-token" });

        Assert.NotNull(map);
        Assert.Equal(5, map.Rows.Count);
        Assert.Equal(1m, map.Rows[0].BlastRadius);
    }

    private static FragilityRow Row(FragilityMap map, ArchitectureGraph graph, string name) =>
        map.Rows.Single(r => r.NodeId == graph.NodeId(name));

    private static IReadOnlyList<FindingElement> Elements(SimulationResult result, HealthState state) => result.States
        .Where(s => s.State == state)
        .Select(s => new FindingElement(s.Type, s.Id))
        .ToList();
}
