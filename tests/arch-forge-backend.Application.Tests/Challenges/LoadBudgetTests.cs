using System.Text.Json;
using arch_forge_backend.Application.Challenges;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.TestDoubles;
using Xunit;

namespace arch_forge_backend.Application.Tests.Challenges;

public class LoadBudgetTests
{
    public static TheoryData<string, int> Budgets() => new()
    {
        { "database-overload", 18 },
        { "latency-budget", 30 },
        { "million-a-day", 42 },
        { "it-does-not-fit", 24 },
        { "no-new-hardware", 18 }
    };

    [Theory]
    [MemberData(nameof(Budgets))]
    public void The_five_load_challenges_are_the_ones_played_with_a_budget(string slug, int max)
    {
        var budget = Assert.Single(
            ChallengeHarness.Catalog.Find(slug)!.Requirements,
            r => r.Kind == RequirementKinds.MaxCost);

        Assert.Equal(max, budget.Max);
    }

    [Fact]
    public void No_other_challenge_is_taxed_with_one()
    {
        var priced = ChallengeHarness.Catalog.All
            .Where(c => c.Requirements.Concat(c.OptionalGoals).Any(r => r.Kind == RequirementKinds.MaxCost))
            .Select(c => c.Slug)
            .ToList();

        Assert.Equal(Budgets().Select(row => (string)row[0]!).Order(), priced.Order());
    }

    [Theory]
    [MemberData(nameof(Budgets))]
    public void The_reference_solution_fits_with_room_for_nothing_but_a_cheap_box(string slug, int max)
    {
        var cost = CostModel.Of(ChallengeHarness.Graph(ChallengeHarness.Catalog.ReferenceSolutionOf(slug)!));

        Assert.True(cost <= max, $"'{slug}' costs {cost} and is played with {max}");
        Assert.Equal(2, max - cost);
    }

    [Theory]
    [MemberData(nameof(Budgets))]
    public void One_more_copy_of_the_cheapest_thing_you_could_scale_no_longer_fits(string slug, int max)
    {
        var solution = ChallengeHarness.Catalog.ReferenceSolutionOf(slug)!;
        var copy = solution.Nodes
            .Where(n => Scalable(n.Type))
            .Min(n => CostModel.PerInstance(n.Type));

        Assert.True(
            CostModel.Of(ChallengeHarness.Graph(solution)) + copy > max,
            $"'{slug}' still affords another copy of something ({copy})");
    }

    public static TheoryData<string, int> BudgetsWithADatabase() => new()
    {
        { "database-overload", 18 },
        { "latency-budget", 30 },
        { "million-a-day", 42 },
        { "no-new-hardware", 18 }
    };

    [Theory]
    [MemberData(nameof(BudgetsWithADatabase))]
    public void Buying_a_read_replica_on_top_is_what_the_budget_stops(string slug, int max)
    {
        var solution = ChallengeHarness.Catalog.ReferenceSolutionOf(slug)!;
        var bought = Scaled(solution);

        Assert.True(CostModel.Of(ChallengeHarness.Graph(bought)) > max);

        var result = ChallengeHarness.Evaluate(slug, bought);

        Assert.False(result.Passed);
        Assert.False(Assert.Single(result.Requirements, r => r.Kind == RequirementKinds.MaxCost).Met);
    }

    [Fact]
    public void The_reference_solution_of_it_does_not_fit_is_the_smallest_thing_that_fits()
    {
        var graph = ChallengeHarness.Graph(ChallengeHarness.Catalog.ReferenceSolutionOf("it-does-not-fit")!);
        var load = LoadModel.Of(graph);

        Assert.Equal(22, CostModel.Of(graph));

        foreach (var node in graph.Nodes.Where(n => NodeSettings.Instances(n) > 1))
        {
            var copies = NodeSettings.Instances(node);
            var oneFewer = load.UtilizationOf(node.Id) * copies / (copies - 1);

            Assert.True(oneFewer > 0.8m, $"'{node.Name}' does not need its {copies}th copy");
        }
    }

    [Fact]
    public void Deleting_the_search_engine_is_the_cheapest_thing_that_fits_and_it_does_not_pass()
    {
        var solution = ChallengeHarness.Catalog.ReferenceSolutionOf("it-does-not-fit")!;
        var mutilated = new ArchitectureBlueprint
        {
            Name = solution.Name,
            Nodes = solution.Nodes.Where(n => n.Name != "Search").ToList(),
            Connections = solution.Connections.Where(c => c.To != "Search" && c.From != "Search").ToList()
        };

        Assert.True(CostModel.Of(ChallengeHarness.Graph(mutilated)) < 24);

        var result = ChallengeHarness.Evaluate("it-does-not-fit", mutilated);

        Assert.False(result.Passed);
        Assert.False(Assert.Single(result.Requirements, r => r.Kind == RequirementKinds.ContainsNode).Met);
    }

    [Fact]
    public void The_budget_of_no_new_hardware_says_what_the_two_caps_it_replaced_said()
    {
        var definition = ChallengeHarness.Catalog.Find("no-new-hardware")!;

        Assert.DoesNotContain(definition.Requirements, r => r.Kind == RequirementKinds.MaxNodes);
        Assert.DoesNotContain(definition.Requirements, r => r.Kind == RequirementKinds.MaxInstances);

        var solution = ChallengeHarness.Catalog.ReferenceSolutionOf("no-new-hardware")!;
        var cheapestCopy = solution.Nodes.Where(n => Scalable(n.Type)).Min(n => CostModel.PerInstance(n.Type));

        Assert.Equal(16, CostModel.Of(ChallengeHarness.Graph(solution)));
        Assert.True(16 + cheapestCopy > 18, "a second copy of something still fits in the quarter");
    }

    // The four types whose instance count is a lever a solver can pull; everything else costs one copy
    // and no more, so the budget's two units of slack are a cheap box and never a second engine.
    private static bool Scalable(Domain.Enums.NodeType type) => type is
        Domain.Enums.NodeType.Api or Domain.Enums.NodeType.Service or
        Domain.Enums.NodeType.Worker or Domain.Enums.NodeType.Database;

    private static ArchitectureBlueprint Scaled(ArchitectureBlueprint blueprint) => new()
    {
        Name = blueprint.Name,
        Nodes = blueprint.Nodes.Select(n => new BlueprintNode
        {
            Name = n.Name,
            Type = n.Type,
            X = n.X,
            Y = n.Y,
            Configuration = Doubled(n)
        }).ToList(),
        Connections = blueprint.Connections
    };

    private static JsonElement? Doubled(BlueprintNode node)
    {
        if (node.Type != Domain.Enums.NodeType.Database)
            return node.Configuration;

        var settings = node.Configuration is { } declared
            ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(declared.GetRawText())!
            : new Dictionary<string, JsonElement>();

        var replicas = settings.TryGetValue("readReplicas", out var current) ? current.GetInt32() : 0;
        settings["readReplicas"] = JsonDocument.Parse($"{replicas + 1}").RootElement.Clone();

        return JsonDocument.Parse(JsonSerializer.Serialize(settings)).RootElement.Clone();
    }
}
