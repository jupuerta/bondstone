using System.Text.Json;
using System.Text.Json.Serialization;
using arch_forge_backend.Application.Challenges;
using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Challenges;

public class ZoneFaultShapeTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void A_challenge_asks_for_a_downed_zone_in_the_shape_the_contract_fixes()
    {
        var fault = JsonSerializer.Deserialize<ScenarioFault>("""{ "zone": "a", "kind": "down" }""", Options)!;

        Assert.Equal("a", fault.Zone);
        Assert.Equal(FaultKinds.Down, fault.Kind);
        Assert.Null(fault.Node);
        Assert.True(fault.NamesAZone);
        Assert.False(fault.NamesANode);
    }

    [Fact]
    public void The_twenty_two_challenges_that_name_a_node_keep_reading_as_they_did()
    {
        var fault = JsonSerializer.Deserialize<ScenarioFault>("""{ "node": "Db", "kind": "down" }""", Options)!;

        Assert.Equal("Db", fault.Node);
        Assert.Null(fault.Zone);
        Assert.True(fault.NamesANode);
        Assert.False(fault.NamesAZone);
    }
}

public class ZoneFaultValidationTests
{
    private static ChallengeRequirement Over(params ScenarioFault[] faults) => new()
    {
        Kind = RequirementKinds.UnderScenario,
        LabelKey = "challenge.x.y",
        Faults = [.. faults],
        Metric = RequirementMetrics.EntryPointsAffected,
        Operator = RequirementOperators.Equal,
        Value = 0
    };

    private static string Rejects(ChallengeRequirement requirement) =>
        Assert.Throws<InvalidChallengeDefinitionException>(
            () => ChallengeCatalog.ValidateRequirement("x", requirement)).Message;

    [Fact]
    public void A_zone_a_b_or_c_is_accepted()
    {
        foreach (var zone in new[] { "a", "b", "c" })
            ChallengeCatalog.ValidateRequirement("x", Over(new ScenarioFault(null, FaultKinds.Down, Zone: zone)));
    }

    [Fact]
    public void A_fault_on_neither_a_node_nor_a_zone_brings_the_catalogue_down()
    {
        Assert.Contains("neither a node nor a zone", Rejects(Over(new ScenarioFault(null, FaultKinds.Down))));
        Assert.Contains("neither a node nor a zone", Rejects(Over(new ScenarioFault("  ", FaultKinds.Down))));
    }

    [Fact]
    public void A_fault_on_both_a_node_and_a_zone_brings_the_catalogue_down()
    {
        Assert.Contains(
            "one or the other",
            Rejects(Over(new ScenarioFault("Db", FaultKinds.Down, Zone: "a"))));
    }

    [Fact]
    public void A_zone_outside_a_b_c_brings_the_catalogue_down()
    {
        Assert.Contains("not one of a, b, c", Rejects(Over(new ScenarioFault(null, FaultKinds.Down, Zone: "d"))));
        Assert.Contains("not one of a, b, c", Rejects(Over(new ScenarioFault(null, FaultKinds.Down, Zone: "A"))));
    }

    [Fact]
    public void The_only_thing_that_happens_to_a_zone_is_that_it_goes_down()
    {
        Assert.Contains("goes down", Rejects(Over(new ScenarioFault(null, FaultKinds.Degraded, Zone: "a"))));
        Assert.Contains("goes down", Rejects(Over(new ScenarioFault(null, FaultKinds.Latency, 500, "a"))));

        ChallengeCatalog.ValidateRequirement("x", Over(new ScenarioFault("Db", FaultKinds.Degraded)));
    }
}

public class ZoneFaultResolutionTests
{
    // The shape the product already teaches in single-point-of-failure, with the two Apis placed:
    // Gw -> Lb -> {Api in a, Api 2 in b} -> Db. The load balancer is what makes surviving a sibling
    // real, and the zone is what decides whether the two siblings die together.
    private static ArchitectureBlueprint TwoApis(string second = """{"zone":"b"}""") => Blueprint.Of(
        [("Gw", NodeType.Gateway, "{}"),
         ("Lb", NodeType.LoadBalancer, "{}"),
         ("Api", NodeType.Api, """{"zone":"a"}"""),
         ("Api 2", NodeType.Api, second),
         ("Db", NodeType.Database, "{}")],
        [("Gw", "Lb", true, """{"timeoutSeconds":2}"""),
         ("Lb", "Api", true, """{"timeoutSeconds":2}"""),
         ("Lb", "Api 2", true, """{"timeoutSeconds":2}"""),
         ("Api", "Db", true, """{"timeoutSeconds":5}"""),
         ("Api 2", "Db", true, """{"timeoutSeconds":5}""")]);

    [Fact]
    public void A_zone_travels_as_a_name_and_is_not_expanded_here()
    {
        var context = ChallengeHarness.Context(TwoApis());

        Assert.True(context.TryResolve([new ScenarioFault(null, FaultKinds.Down, Zone: "a")], out var scenario, out var missing));

        Assert.Null(missing);
        Assert.Empty(scenario.Faults);
        Assert.Equal(["a"], scenario.Zones);
    }

    [Fact]
    public void A_scenario_can_name_a_node_and_a_zone_at_once()
    {
        var context = ChallengeHarness.Context(TwoApis());

        Assert.True(context.TryResolve(
            [new ScenarioFault("Db", FaultKinds.Down), new ScenarioFault(null, FaultKinds.Down, Zone: "a")],
            out var scenario,
            out _));

        Assert.Single(scenario.Faults);
        Assert.Equal(["a"], scenario.Zones);
    }

    [Fact]
    public void A_missing_node_still_stops_the_scenario_and_a_zone_never_does()
    {
        var context = ChallengeHarness.Context(TwoApis());

        Assert.False(context.TryResolve([new ScenarioFault("Payments", FaultKinds.Down)], out _, out var missing));
        Assert.Equal("Payments", missing);

        Assert.True(context.TryResolve([new ScenarioFault(null, FaultKinds.Down, Zone: "c")], out var empty, out _));
        Assert.Equal(["c"], empty.Zones);
    }

    [Fact]
    public void Two_different_zones_are_two_different_simulations()
    {
        var context = ChallengeHarness.Context(TwoApis());

        ChallengeRequirement Over(string zone) => new()
        {
            Kind = RequirementKinds.UnderScenario,
            LabelKey = zone,
            Faults = [new ScenarioFault(null, FaultKinds.Down, Zone: zone)],
            Metric = RequirementMetrics.ComponentsDown,
            Operator = RequirementOperators.GreaterOrEqual,
            Value = 0
        };

        RequirementEvaluator.Evaluate(Over("a"), context);
        RequirementEvaluator.Evaluate(Over("b"), context);
        RequirementEvaluator.Evaluate(Over("a"), context);

        Assert.Equal(2, context.SimulationsRun);
    }

    [Fact]
    public void The_lesson_the_requirement_exists_to_teach()
    {
        // Two Apis spread over a and b survive zone a going down: one component dies, the entry point
        // degrades and keeps serving. Both crammed into zone a and the whole site goes with it — five
        // components down, blast radius 1. That gap is the reason the zone fault exists.
        ChallengeRequirement AtMost(decimal down) => new()
        {
            Kind = RequirementKinds.UnderScenario,
            LabelKey = "challenge.x.y",
            Faults = [new ScenarioFault(null, FaultKinds.Down, Zone: "a")],
            Metric = RequirementMetrics.ComponentsDown,
            Operator = RequirementOperators.LessOrEqual,
            Value = down
        };

        var spread = ChallengeHarness.Context(TwoApis());
        var crammed = ChallengeHarness.Context(TwoApis("""{"zone":"a"}"""));

        Assert.True(RequirementEvaluator.Evaluate(AtMost(1), spread).Met);
        Assert.False(RequirementEvaluator.Evaluate(AtMost(1), crammed).Met);

        var detail = RequirementEvaluator.Evaluate(AtMost(1), crammed).Detail!;
        Assert.Equal("metric.componentsDown", detail.Arg("metric"));
    }

    [Fact]
    public void A_zone_nobody_is_placed_in_is_a_scenario_that_does_nothing()
    {
        var requirement = new ChallengeRequirement
        {
            Kind = RequirementKinds.UnderScenario,
            LabelKey = "challenge.x.y",
            Faults = [new ScenarioFault(null, FaultKinds.Down, Zone: "c")],
            Metric = RequirementMetrics.ComponentsDown,
            Operator = RequirementOperators.Equal,
            Value = 0
        };

        // Nothing sits in c, so nothing falls — and nothing is reported as discarded either, because a
        // discarded fault is a Fault and a zone has no Guid to put in one.
        Assert.True(RequirementEvaluator.Evaluate(requirement, ChallengeHarness.Context(TwoApis())).Met);
    }
}
