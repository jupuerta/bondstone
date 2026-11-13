using bondstone_backend.Application.Tests.TestDoubles;
using bondstone_backend.Application.Localization;
using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Challenges;
using bondstone_backend.Application.Simulation;
using bondstone_backend.Domain.Enums;
using Xunit;

namespace bondstone_backend.Application.Tests.Challenges;

public class DatabaseOverloadTests
{
    private const string Slug = "database-overload";

    [Fact]
    public void The_starter_is_the_number_the_story_tells()
    {
        var run = Figures(ChallengeHarness.Catalog.StarterOf(Slug)!);

        Assert.Equal(1.5m, run.Utilization("Db"));
        Assert.Equal(0.3333m, run.Metrics.RequestsAffected);
        Assert.Equal(615, run.Metrics.NominalLatencyMs);
    }

    [Fact]
    public void A_cache_is_what_moves_every_one_of_those_numbers()
    {
        var run = Figures(ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!);

        Assert.Equal(0.3m, run.Utilization("Db"));
        Assert.Equal(0m, run.Metrics.RequestsAffected);
        Assert.Equal(50, run.Metrics.NominalLatencyMs);
    }

    [Fact]
    public void Declaring_less_traffic_is_not_a_solution()
    {
        var quiet = WithLoad(ChallengeHarness.Catalog.StarterOf(Slug)!, 1);
        var result = ChallengeHarness.Evaluate(Slug, quiet);

        Assert.False(result.Passed);
        var declared = Assert.Single(result.Requirements, r => r.Kind == RequirementKinds.DeclaresLoad);
        Assert.False(declared.Met);
        Assert.Equal("300", declared.Detail.Arg("min"));
    }

    [Fact]
    public void The_optional_goal_asks_for_the_lesson_of_the_previous_challenge()
    {
        var result = ChallengeHarness.Evaluate(Slug, ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!);

        Assert.All(result.OptionalGoals, goal => Assert.True(goal.Met, goal.Detail.Describe()));
    }

    internal static LoadFigures Figures(ArchitectureBlueprint blueprint) => LoadFigures.Of(blueprint);

    internal static ArchitectureBlueprint WithLoad(ArchitectureBlueprint blueprint, int rps)
    {
        var copy = new ArchitectureBlueprint
        {
            Name = blueprint.Name,
            Nodes = blueprint.Nodes.Select(n => new BlueprintNode
            {
                Name = n.Name,
                Type = n.Type,
                X = n.X,
                Y = n.Y,
                Configuration = n.Name == blueprint.Nodes[0].Name
                    ? System.Text.Json.JsonDocument.Parse($$"""{"requestsPerSecond":{{rps}}}""").RootElement.Clone()
                    : n.Configuration
            }).ToList(),
            Connections = blueprint.Connections
        };

        return copy;
    }
}

public class LatencyBudgetTests
{
    private const string Slug = "latency-budget";

    [Fact]
    public void The_starter_opens_at_the_number_the_title_says()
    {
        var run = LoadFigures.Of(ChallengeHarness.Catalog.StarterOf(Slug)!);

        Assert.Equal(820, run.Metrics.NominalLatencyMs);
        Assert.Equal(1m, run.Utilization("Catalog"));
        Assert.Equal(1.5m, run.Utilization("Db"));
    }

    [Fact]
    public void The_solution_buys_the_latency_with_margin_and_not_with_milliseconds()
    {
        var run = LoadFigures.Of(ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!);

        Assert.Equal(69, run.Metrics.NominalLatencyMs);
        Assert.Equal(0.5m, run.Utilization("Catalog"));
        Assert.Equal(0.3m, run.Utilization("Db"));
    }

    [Fact]
    public void Deleting_the_database_is_not_a_way_to_go_faster()
    {
        var starter = ChallengeHarness.Catalog.StarterOf(Slug)!;
        var withoutDb = new ArchitectureBlueprint
        {
            Name = starter.Name,
            Nodes = starter.Nodes.Where(n => n.Type != NodeType.Database).ToList(),
            Connections = starter.Connections.Where(c => c.To != "Db").ToList()
        };

        var result = ChallengeHarness.Evaluate(Slug, withoutDb);

        Assert.False(result.Passed);
        Assert.False(Assert.Single(result.Requirements, r => r.Kind == RequirementKinds.ContainsNode).Met);
    }
}

public class MillionADayTests
{
    private const string Slug = "million-a-day";

    [Fact]
    public void The_starter_is_missing_the_pieces_the_brief_asked_for()
    {
        var result = ChallengeHarness.Evaluate(Slug, ChallengeHarness.Catalog.StarterOf(Slug)!);

        Assert.Equal(3, result.Requirements.Count(r => !r.Met));
        Assert.True(Capacity(result).Met);
    }

    [Fact]
    public void Adding_what_the_brief_asked_for_is_what_puts_the_database_over_capacity()
    {
        var honest = WithoutReadReplicas(ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!);
        var run = LoadFigures.Of(honest);

        Assert.Equal(232m, run.LoadRps("Db"));
        Assert.Equal(1.16m, run.Utilization("Db"));

        var result = ChallengeHarness.Evaluate(Slug, honest);
        Assert.False(result.Passed);
        Assert.False(Capacity(result).Met);
    }

    private static ChallengeRequirementResult Capacity(ChallengeResult result) =>
        Assert.Single(result.Requirements, r => r.Label.Key == $"challenge.{Slug}.requirement.5");

    [Fact]
    public void One_read_replica_is_what_closes_it()
    {
        var run = LoadFigures.Of(ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!);

        Assert.Equal(232m, run.LoadRps("Db"));
        Assert.Equal(0.58m, run.Utilization("Db"));

        var result = ChallengeHarness.Evaluate(Slug, ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!);
        Assert.True(result.Passed);

        Assert.All(result.OptionalGoals, goal => Assert.True(goal.Met, goal.Detail.Describe()));
    }

    private static ArchitectureBlueprint WithoutReadReplicas(ArchitectureBlueprint blueprint) => new()
    {
        Name = blueprint.Name,
        Nodes = blueprint.Nodes.Select(n => new BlueprintNode
        {
            Name = n.Name,
            Type = n.Type,
            X = n.X,
            Y = n.Y,
            Configuration = n.Type == NodeType.Database ? null : n.Configuration
        }).ToList(),
        Connections = blueprint.Connections
    };
}

internal sealed class LoadFigures
{
    private LoadFigures(ArchitectureGraph graph, LoadModel load, SimulationMetrics metrics)
    {
        Graph = graph;
        Load = load;
        Metrics = metrics;
    }

    public ArchitectureGraph Graph { get; }
    public LoadModel Load { get; }
    public SimulationMetrics Metrics { get; }

    public static LoadFigures Of(ArchitectureBlueprint blueprint)
    {
        var architecture = blueprint.Instantiate("test");
        var graph = new ArchitectureGraph(architecture.Nodes, architecture.Connections);

        return new LoadFigures(
            graph,
            LoadModel.Of(graph),
            new SimulationEngine().Run(graph, SimulationScenario.Empty).Metrics);
    }

    public decimal Utilization(string name) => Load.UtilizationOf(IdOf(name));

    public decimal LoadRps(string name) => Load.LoadOf(IdOf(name));

    private Guid IdOf(string name) => Graph.Nodes.Single(n => n.Name == name).Id;
}
