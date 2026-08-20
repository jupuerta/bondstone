using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Challenges;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Challenges;

/// <summary>
/// V4 VS4. The three challenges that need the load axis, and the numbers each one teaches. Worked
/// out by hand from the model before the files existed - which is why `latency-budget` opens at
/// 820 ms and not at some number that happened to come out of a run.
/// </summary>
public class DatabaseOverloadTests
{
    private const string Slug = "database-overload";

    [Fact]
    public void The_starter_is_the_number_the_story_tells()
    {
        var run = Figures(ChallengeHarness.Catalog.StarterOf(Slug)!);

        // 300 rps into a database that takes 200: 150 % of its capacity, a third of the requests
        // rejected, and half a second to answer the rest.
        Assert.Equal(1.5m, run.Utilization("Db"));
        Assert.Equal(0.3333m, run.Metrics.RequestsAffected);
        Assert.Equal(615, run.Metrics.NominalLatencyMs);
    }

    [Fact]
    public void A_cache_is_what_moves_every_one_of_those_numbers()
    {
        var run = Figures(ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!);

        // The same 300 rps arrive; a fifth of them reach the database. Nothing was made bigger.
        Assert.Equal(0.3m, run.Utilization("Db"));
        Assert.Equal(0m, run.Metrics.RequestsAffected);
        Assert.Equal(50, run.Metrics.NominalLatencyMs);
    }

    [Fact]
    public void Declaring_less_traffic_is_not_a_solution()
    {
        // The anti-cheese of `declaresLoad`: traffic is a property of the architecture (DT.1), so
        // the box is editable, and without this the challenge is passed by typing 1 into it.
        var quiet = WithLoad(ChallengeHarness.Catalog.StarterOf(Slug)!, 1);
        var result = ChallengeHarness.Evaluate(Slug, quiet);

        Assert.False(result.Passed);
        var declared = Assert.Single(result.Requirements, r => r.Kind == RequirementKinds.DeclaresLoad);
        Assert.False(declared.Met);
        Assert.Contains("300", declared.Detail.Es());
    }

    [Fact]
    public void The_optional_goal_asks_for_the_lesson_of_the_previous_challenge()
    {
        // The cache stops the database being a bottleneck; the fallback stops it being a single
        // point of failure. Two different problems, and the reference solution answers both.
        var result = ChallengeHarness.Evaluate(Slug, ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!);

        Assert.All(result.OptionalGoals, goal => Assert.True(goal.Met, goal.Detail.Es()));
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

        // Storefront 13 + Http 5 + Catalog 200 + Sql 2 + Db 600. The 800 ms of §12, and none of it
        // is a constant somebody typed: it is what being at 100 % and at 150 % costs (DT.6).
        Assert.Equal(820, run.Metrics.NominalLatencyMs);
        Assert.Equal(1m, run.Utilization("Catalog"));
        Assert.Equal(1.5m, run.Utilization("Db"));
    }

    [Fact]
    public void The_solution_buys_the_latency_with_margin_and_not_with_milliseconds()
    {
        var run = LoadFigures.Of(ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!);

        // 820 → 69 ms without touching a single latency constant. Two instances of each service
        // halve their utilisation, and the cache takes the database off the hot path.
        Assert.Equal(69, run.Metrics.NominalLatencyMs);
        Assert.Equal(0.5m, run.Utilization("Catalog"));
        Assert.Equal(0.3m, run.Utilization("Db"));
    }

    [Fact]
    public void Deleting_the_database_is_not_a_way_to_go_faster()
    {
        // The obvious cheat on a latency challenge, and the reason `containsNode Database` is one
        // of the three requirements rather than an afterthought.
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

        // Three of the five: no authentication, nowhere to put files, nothing running in the
        // background. At 116 rps nothing is short yet - that comes later, and it is the point.
        Assert.Equal(3, result.Requirements.Count(r => !r.Met));
        // V7 D1.1: a requirement is picked out by its key, not by a word in its label. The label's
        // prose lives in the client's catalogue - the backend never renders challenge text - so
        // matching on "capacidad" would be matching on something this side no longer has.
        Assert.True(Capacity(result).Met);
    }

    [Fact]
    public void Adding_what_the_brief_asked_for_is_what_puts_the_database_over_capacity()
    {
        // **The arc of the challenge.** The background worker queries the same database as the API,
        // so honouring the brief takes it from 116 rps to 232 - past the 200 it takes. You do what
        // you were told and *that* is what breaks it, which is how capacity problems actually
        // arrive.
        var honest = WithoutReadReplicas(ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!);
        var run = LoadFigures.Of(honest);

        Assert.Equal(232m, run.LoadRps("Db"));
        Assert.Equal(1.16m, run.Utilization("Db"));

        var result = ChallengeHarness.Evaluate(Slug, honest);
        Assert.False(result.Passed);
        Assert.False(Capacity(result).Met);
    }

    /// <summary>The "no component goes over 80 % of its capacity at peak" requirement.</summary>
    private static ChallengeRequirementResult Capacity(ChallengeResult result) =>
        Assert.Single(result.Requirements, r => r.Label.Key == $"challenge.{Slug}.requirement.5");

    [Fact]
    public void One_read_replica_is_what_closes_it()
    {
        var run = LoadFigures.Of(ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!);

        // The same 232 rps against 400 of capacity: 58 %, with room for the next peak.
        Assert.Equal(232m, run.LoadRps("Db"));
        Assert.Equal(0.58m, run.Utilization("Db"));

        var result = ChallengeHarness.Evaluate(Slug, ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!);
        Assert.True(result.Passed);

        // And both extras: a rate limit at the door and something watching (D5.2's new rules).
        Assert.All(result.OptionalGoals, goal => Assert.True(goal.Met, goal.Detail.Es()));
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

/// <summary>Runs the engine over a blueprint and lets the assertions read by component name.</summary>
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
