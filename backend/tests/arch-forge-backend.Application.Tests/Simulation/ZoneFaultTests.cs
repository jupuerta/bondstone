using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Architectures.Queries;
using bondstone_backend.Application.Common.Exceptions;
using bondstone_backend.Application.Simulation;
using bondstone_backend.Application.Tests.Analysis.Fixtures;
using bondstone_backend.Application.Tests.Analysis.Rules.Support;
using bondstone_backend.Application.Tests.TestDoubles;
using bondstone_backend.Domain.Enums;
using Xunit;

namespace bondstone_backend.Application.Tests.Simulation;

public class ZoneFaultTests
{
    private static GraphBuilder ThreeZones() => new GraphBuilder()
        .Node("Client", NodeType.Client)
        .Node("LB", NodeType.LoadBalancer, """{"healthCheck":true}""")
        .Node("ApiA", NodeType.Api, """{"zone":"a"}""")
        .Node("ApiB", NodeType.Api, """{"zone":"b"}""")
        .Node("CacheA", NodeType.Cache, """{"zone":"a"}""")
        .Node("Db", NodeType.Database)
        .Connect("Client", "LB")
        .Connect("LB", "ApiA")
        .Connect("LB", "ApiB")
        .Connect("ApiA", "CacheA")
        .Connect("ApiB", "Db")
        .Connect("CacheA", "Db");


    [Fact]
    public void A_downed_zone_becomes_one_down_fault_per_component_placed_in_it()
    {
        var builder = ThreeZones();
        var graph = builder.Build();

        var (applicable, discarded) = new SimulationScenario([], ["a"]).Resolve(graph);

        Assert.Empty(discarded);
        Assert.Equal(
            [builder.IdOf("ApiA"), builder.IdOf("CacheA")],
            applicable.Faults.Select(f => f.Id).OrderBy(id => id));

        Assert.All(applicable.Faults, fault =>
        {
            Assert.Equal(ElementType.Node, fault.Target);
            Assert.Equal(FaultKinds.Down, fault.Kind);
            Assert.Null(fault.Value);
        });
    }

    [Fact]
    public void The_engine_never_sees_the_zone_only_the_components_it_took_with_it()
    {
        var builder = ThreeZones();
        var graph = builder.Build();

        var byZone = new SimulationEngine().Run(graph, new SimulationScenario([], ["a"]));

        var byHand = new SimulationEngine().Run(graph, new SimulationScenario(
        [
            new Fault(ElementType.Node, builder.IdOf("ApiA"), FaultKinds.Down),
            new Fault(ElementType.Node, builder.IdOf("CacheA"), FaultKinds.Down)
        ]));

        Assert.Equal(byHand.Metrics, byZone.Metrics);
        Assert.Equal(byHand.States, byZone.States);
        Assert.Equal(byHand.DiscardedFaults, byZone.DiscardedFaults);
    }

    [Fact]
    public void A_component_without_a_zone_is_never_taken_down_by_one()
    {
        var builder = ThreeZones();
        var graph = builder.Build();

        var (applicable, _) = new SimulationScenario([], ["a", "b", "c"]).Resolve(graph);

        var touched = applicable.Faults.Select(f => f.Id).ToHashSet();

        Assert.DoesNotContain(builder.IdOf("Db"), touched);
        Assert.DoesNotContain(builder.IdOf("Client"), touched);
        Assert.DoesNotContain(builder.IdOf("LB"), touched);
    }

    [Fact]
    public void A_type_that_is_not_yours_to_place_ignores_its_zone_here_too()
    {
        var builder = new GraphBuilder()
            .Node("Client", NodeType.Client, """{"zone":"a"}""")
            .Node("Cdn", NodeType.Cdn, """{"zone":"a"}""")
            .Node("Api", NodeType.Api, """{"zone":"a"}""")
            .Connect("Client", "Cdn")
            .Connect("Cdn", "Api");

        var (applicable, _) = new SimulationScenario([], ["a"]).Resolve(builder.Build());

        Assert.Equal([builder.IdOf("Api")], applicable.Faults.Select(f => f.Id));
    }

    [Fact]
    public void An_empty_zone_does_nothing_and_is_not_announced_as_discarded()
    {
        var graph = ThreeZones().Build();

        var (applicable, discarded) = new SimulationScenario([], ["c"]).Resolve(graph);

        Assert.Empty(applicable.Faults);
        Assert.Empty(discarded);
    }

    [Fact]
    public void A_component_named_by_hand_and_by_its_zone_falls_once()
    {
        var builder = ThreeZones();
        var byHand = new Fault(ElementType.Node, builder.IdOf("ApiA"), FaultKinds.Down);

        var (applicable, _) = new SimulationScenario([byHand], ["a"]).Resolve(builder.Build());

        Assert.Equal(2, applicable.Faults.Count);
        Assert.Single(applicable.Faults, f => f.Id == builder.IdOf("ApiA"));
    }

    [Fact]
    public void A_scenario_with_no_zone_resolves_exactly_as_it_did_before()
    {
        var builder = ThreeZones();
        var graph = builder.Build();
        var fault = new Fault(ElementType.Node, builder.IdOf("Db"), FaultKinds.Down);
        var ghost = new Fault(ElementType.Node, Guid.NewGuid(), FaultKinds.Down);

        var (applicable, discarded) = new SimulationScenario([ghost, fault]).Resolve(graph);

        Assert.Equal([fault], applicable.Faults);
        Assert.Equal([ghost], discarded);
        Assert.Empty(applicable.Zones);
    }

    [Fact]
    public void Resolving_is_stable_however_the_zones_were_written()
    {
        var graph = ThreeZones().Build();

        var one = new SimulationScenario([], ["b", "a", "a"]).Resolve(graph).Applicable;
        var other = new SimulationScenario([], ["a", "b"]).Resolve(graph).Applicable;

        Assert.Equal(other.Faults, one.Faults);
        Assert.Equal(["a", "b"], one.Zones);
    }

    [Fact]
    public void The_reference_architectures_declare_no_zone_so_no_zone_can_reach_them()
    {
        foreach (var name in new[] { "minimal-api-db", "gateway-microservices", "resilient-async", "worst-case" })
        {
            var graph = ReferenceArchitecture.Graph(name);
            var (applicable, discarded) = new SimulationScenario([], ["a", "b", "c"]).Resolve(graph);

            Assert.Empty(applicable.Faults);
            Assert.Empty(discarded);
        }
    }

    [Fact]
    public void Only_down_can_happen_to_a_zone()
    {
        Assert.True(FaultKinds.AppliesToZone(FaultKinds.Down));
        Assert.False(FaultKinds.AppliesToZone(FaultKinds.Degraded));
        Assert.False(FaultKinds.AppliesToZone(FaultKinds.Latency));
    }

    [Fact]
    public async Task A_zone_outside_the_closed_set_is_refused_at_the_door()
    {
        var query = new SimulateArchitectureQuery
        {
            ShareToken = "token",
            Scenario = new SimulationScenario([], ["d"])
        };

        var handler = new SimulateArchitectureQueryHandler(new FakeArchitectureRepository(), new SimulationEngine());

        var error = await Assert.ThrowsAsync<InvalidSimulationScenarioException>(() => handler.Handle(query));
        Assert.Contains("'d' is not a known zone", error.Message);
    }

    [Fact]
    public async Task The_same_zone_twice_is_refused_at_the_door()
    {
        var query = new SimulateArchitectureQuery
        {
            ShareToken = "token",
            Scenario = new SimulationScenario([], ["a", "a"])
        };

        var handler = new SimulateArchitectureQueryHandler(new FakeArchitectureRepository(), new SimulationEngine());

        var error = await Assert.ThrowsAsync<InvalidSimulationScenarioException>(() => handler.Handle(query));
        Assert.Contains("listed twice", error.Message);
    }
}
