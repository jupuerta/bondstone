using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Challenges;
using arch_forge_backend.Application.Simulation;
using System.Text.Json;
using Xunit;

namespace arch_forge_backend.Application.Tests.Challenges;

public class ChallengeDefinitionsTests
{
    public static TheoryData<string> Slugs()
    {
        var data = new TheoryData<string>();
        foreach (var challenge in ChallengeHarness.Catalog.All)
            data.Add(challenge.Slug);
        return data;
    }

    [Fact]
    public void The_catalogue_loads()
    {
        Assert.NotEmpty(ChallengeHarness.Catalog.All);
    }


    [Fact]
    public void Only_a_latency_ceiling_over_an_empty_scenario_publishes_its_budget()
    {
        var published = ChallengeHarness.Catalog.All
            .Select(c => c.ToDetailDto())
            .SelectMany(d => d.Requirements.Concat(d.OptionalGoals).Select(r => (d.Slug, r)))
            .Where(pair => pair.r.LatencyBudgetMs is not null)
            .Select(pair => (pair.Slug, pair.r.Kind, Budget: pair.r.LatencyBudgetMs!.Value))
            .ToList();

        Assert.Equal(
            [
                ("latency-budget", RequirementKinds.UnderScenario, 100),
                ("latency-budget", RequirementKinds.UnderScenario, 75),
                ("no-new-hardware", RequirementKinds.UnderScenario, 60),
                ("timeout-was-enough", RequirementKinds.UnderScenario, 400),
            ],
            published);
    }

    [Fact]
    public void A_budget_graded_under_injected_faults_publishes_nothing()
    {
        foreach (var slug in new[] { "retry-storm", "third-party-outage" })
        {
            var detail = ChallengeHarness.Catalog.Find(slug)!.ToDetailDto();
            var latency = detail.Requirements.Concat(detail.OptionalGoals)
                .Where(r => r.Kind == RequirementKinds.UnderScenario)
                .ToList();

            Assert.NotEmpty(latency);
            Assert.All(latency, r => Assert.Null(r.LatencyBudgetMs));
        }
    }

    [Fact]
    public void Nothing_else_a_requirement_knows_travels_with_it()
    {
        var fields = typeof(ChallengeRequirementDto).GetProperties().Select(p => p.Name).ToList();

        Assert.Equal(["Kind", "Label", "LatencyBudgetMs"], fields);
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public void The_reference_solution_passes(string slug)
    {
        var result = ChallengeHarness.Evaluate(slug, ChallengeHarness.Catalog.ReferenceSolutionOf(slug)!);

        Assert.True(
            result.Passed,
            $"'{slug}' is not passed by its own reference solution: " +
            string.Join(" · ", result.Requirements.Where(r => !r.Met).Select(r => $"{r.Label.Key()} — {r.Detail.Describe()}")));
    }

    // The reference solution is what the app hands over when somebody gives up, so it has to be as
    // good as what the challenge asks of them — optional goals included. Without this, a goal can
    // promise something the answer does not deliver: `exposed-database` promised "no criticals" and
    // its own solution left six components unobserved.
    [Theory]
    [MemberData(nameof(Slugs))]
    public void The_reference_solution_meets_its_own_optional_goals(string slug)
    {
        var result = ChallengeHarness.Evaluate(slug, ChallengeHarness.Catalog.ReferenceSolutionOf(slug)!);

        Assert.All(
            result.OptionalGoals,
            goal => Assert.True(
                goal.Met,
                $"'{slug}' asks for '{goal.Label.Key()}' as an optional goal, and its own reference " +
                $"solution does not reach it: {goal.Detail.Describe()}"));
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public void The_starter_does_not_pass(string slug)
    {
        var result = ChallengeHarness.Evaluate(slug, ChallengeHarness.Catalog.StarterOf(slug)!);

        Assert.False(result.Passed, $"'{slug}' is already passed by the architecture it hands you.");
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public void Every_requirement_has_a_reason_when_it_fails(string slug)
    {
        var result = ChallengeHarness.Evaluate(slug, ChallengeHarness.Catalog.StarterOf(slug)!);

        Assert.All(
            result.Requirements.Concat(result.OptionalGoals).Where(r => !r.Met),
            requirement => Assert.NotEmpty(requirement.Detail.Key()));
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public void The_solution_handed_over_when_you_pass_is_the_one_that_passes(string slug)
    {
        var solution = ChallengeHarness.Catalog.ReferenceSolutionOf(slug)!;
        var handedOver = solution.Instantiate("whatever the endpoint names it");

        var result = ChallengeHarness.Evaluate(
            ChallengeHarness.Catalog.Find(slug)!,
            () => new ArchitectureGraph(handedOver.Nodes, handedOver.Connections));

        Assert.True(result.Passed);
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public void Evaluating_is_deterministic(string slug)
    {
        var blueprint = ChallengeHarness.Catalog.ReferenceSolutionOf(slug)!;

        var first = ChallengeHarness.Evaluate(slug, blueprint);
        var second = ChallengeHarness.Evaluate(slug, blueprint);

        Assert.Equal(
            first.Requirements.Select(r => (r.Kind, r.Met, r.Detail.Describe())),
            second.Requirements.Select(r => (r.Kind, r.Met, r.Detail.Describe())));
    }
}

public class ChallengeCatalogueShapeTests
{
    [Fact]
    public void Every_challenge_is_in_one_of_the_three_bands()
    {
        Assert.All(ChallengeHarness.Catalog.All, c => Assert.True(Difficulties.IsKnown(c.Difficulty), c.Slug));
    }

    [Fact]
    public void The_catalogue_reads_as_a_ramp()
    {
        var ranks = ChallengeHarness.Catalog.All.Select(c => Difficulties.Rank(c.Difficulty)).ToList();

        Assert.Equal(ranks.OrderBy(r => r), ranks);

        Assert.Equal(Difficulties.Beginner, ChallengeHarness.Catalog.All[0].Difficulty);
    }

    [Fact]
    public void No_two_challenges_claim_the_same_place_in_the_itinerary()
    {
        var places = ChallengeHarness.Catalog.All.Select(c => (c.Difficulty, c.Order)).ToList();

        Assert.Equal(places.Count, places.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(ChallengeDefinitionsTests.Slugs), MemberType = typeof(ChallengeDefinitionsTests))]
    public void Every_text_key_is_derived_from_the_slug(string slug)
    {
        var definition = ChallengeHarness.Catalog.Find(slug)!;
        var prefix = $"challenge.{slug}.";

        var keys = definition.HintKeys
            .Append(definition.TitleKey)
            .Append(definition.StoryKey)
            .Concat(definition.Requirements.Concat(definition.OptionalGoals).Select(r => r.LabelKey));

        Assert.All(keys, key => Assert.StartsWith(prefix, key, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ChallengeDefinitionsTests.Slugs), MemberType = typeof(ChallengeDefinitionsTests))]
    public void Every_challenge_hands_out_three_hints(string slug)
    {
        Assert.Equal(3, ChallengeHarness.Catalog.Find(slug)!.HintKeys.Count);
    }
}

public class PaymentsFridayTests
{
    private const string Slug = "payments-friday";

    [Fact]
    public void The_starter_falls_apart_when_Payments_goes_down()
    {
        var result = ChallengeHarness.Evaluate(Slug, ChallengeHarness.Catalog.StarterOf(Slug)!);
        var survives = result.Requirements[0];

        Assert.False(survives.Met);

        Assert.Equal("metric.entryPointsAffected.value(value=1)", survives.Detail.Arg("actual"));
        Assert.NotEmpty(survives.Elements);
    }

    [Fact]
    public void The_starter_is_missing_the_timeout_that_makes_everything_else_possible()
    {
        var result = ChallengeHarness.Evaluate(Slug, ChallengeHarness.Catalog.StarterOf(Slug)!);
        var timeouts = result.Requirements[1];

        Assert.False(timeouts.Met);
        Assert.Equal("mechanism.timeout", timeouts.Detail?.Args?["mechanism"] is LocalizedText m ? m.Key : "");

        Assert.Contains("Orders", timeouts.Detail.Arg("calls"));
        Assert.Contains("Payments", timeouts.Detail.Arg("calls"));
        Assert.Single(timeouts.Elements);
    }

    [Fact]
    public void The_solution_contains_the_fault_at_Orders()
    {
        var result = ChallengeHarness.Evaluate(Slug, ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!);

        Assert.True(result.Passed);

        Assert.All(result.OptionalGoals, goal => Assert.True(goal.Met));
    }

    [Fact]
    public void Deleting_Payments_does_not_pass_the_challenge()
    {
        var starter = ChallengeHarness.Catalog.StarterOf(Slug)!;
        var mutilated = new ArchitectureBlueprint
        {
            Name = starter.Name,
            Nodes = starter.Nodes.Where(n => n.Name != "Payments").ToList(),
            Connections = starter.Connections.Where(c => c.To != "Payments").ToList()
        };

        var result = ChallengeHarness.Evaluate(Slug, mutilated);
        var survives = result.Requirements[0];

        Assert.False(result.Passed);
        Assert.False(survives.Met);
        Assert.Contains("Payments", survives.Detail.Describe());
    }
}

public class TimeoutWasEnoughTests
{
    private const string Slug = "timeout-was-enough";

    [Fact]
    public void The_starter_expires_the_call_the_customer_makes()
    {
        var result = ChallengeHarness.Evaluate(Slug, ChallengeHarness.Catalog.StarterOf(Slug)!);
        var fits = result.Requirements[3];

        Assert.False(fits.Met);
        Assert.Contains("TIMEOUT_TOO_SHORT", fits.Detail.Key());
        Assert.Contains("Edge", fits.Detail.Describe());
        Assert.Contains("Storefront", fits.Detail.Describe());
    }

    [Fact]
    public void Nothing_is_over_its_capacity_and_the_second_still_runs_out()
    {
        var analysis = ChallengeHarness.Analyze(ChallengeHarness.Catalog.StarterOf(Slug)!);

        Assert.DoesNotContain(
            analysis.Findings,
            f => f.Code == RuleCodes.UndersizedComponent && f.Severity == Severity.Critical);

        Assert.Contains(
            analysis.Findings,
            f => f.Code == RuleCodes.TimeoutTooShort && f.Severity == Severity.Critical);
    }

    [Fact]
    public void Raising_the_timeout_silences_the_finding_and_fails_the_budget()
    {
        var result = ChallengeHarness.Evaluate(Slug, Retimed(ChallengeHarness.Catalog.StarterOf(Slug)!, 5));

        Assert.True(result.Requirements[3].Met);
        Assert.False(result.Requirements[4].Met);
        Assert.False(result.Passed);
    }

    [Fact]
    public void A_circuit_breaker_makes_the_failure_instant_and_the_call_no_shorter()
    {
        var result = ChallengeHarness.Evaluate(Slug, Broken(ChallengeHarness.Catalog.StarterOf(Slug)!));

        Assert.True(result.Requirements[4].Met);
        Assert.False(result.Requirements[3].Met);
        Assert.False(result.Passed);
    }

    private static ArchitectureBlueprint Retimed(ArchitectureBlueprint blueprint, int seconds) =>
        Reconfigured(blueprint, _ => $"{{\"timeoutSeconds\":{seconds}}}");

    private static ArchitectureBlueprint Broken(ArchitectureBlueprint blueprint) =>
        Reconfigured(blueprint, edge => edge.From == "Edge"
            ? "{\"timeoutSeconds\":1,\"circuitBreaker\":true}"
            : "{\"timeoutSeconds\":1}");

    internal static ArchitectureBlueprint Reconfigured(
        ArchitectureBlueprint blueprint,
        Func<BlueprintConnection, string> configuration) => new()
    {
        Name = blueprint.Name,
        Nodes = blueprint.Nodes,
        Connections = blueprint.Connections
            .Select(c => new BlueprintConnection
            {
                From = c.From,
                To = c.To,
                Synchronous = c.Synchronous,
                Configuration = JsonDocument.Parse(configuration(c)).RootElement.Clone()
            })
            .ToList()
    };
}

public class NobodyNoticedTests
{
    private const string Slug = "nobody-noticed";

    [Fact]
    public void A_fallback_alone_contains_the_damage_and_still_serves_nobody()
    {
        var withFallback = TimeoutWasEnoughTests.Reconfigured(
            ChallengeHarness.Catalog.StarterOf(Slug)!,
            edge => edge.To == "Products"
                ? "{\"timeoutSeconds\":2,\"fallback\":true}"
                : "{\"timeoutSeconds\":2}");

        var metrics = Outage(withFallback);

        Assert.Equal(0, metrics.EntryPointsAffected);
        Assert.True(metrics.BlastRadius < 1m);

        Assert.Equal(1m, metrics.RequestsAffected);
    }

    [Fact]
    public void The_solution_loses_nothing_with_half_the_drawing_tinted()
    {
        var metrics = Outage(ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!);

        Assert.Equal(0m, metrics.RequestsAffected);

        Assert.Equal(1, metrics.ComponentsDown);
        Assert.Equal(2, metrics.ComponentsDegraded);
        Assert.Equal(0.6m, metrics.BlastRadius);
    }

    [Fact]
    public void Deleting_the_database_does_not_pass_the_challenge()
    {
        var starter = ChallengeHarness.Catalog.StarterOf(Slug)!;
        var mutilated = new ArchitectureBlueprint
        {
            Name = starter.Name,
            Nodes = starter.Nodes.Where(n => n.Name != "Products").ToList(),
            Connections = starter.Connections.Where(c => c.To != "Products").ToList()
        };

        var result = ChallengeHarness.Evaluate(Slug, mutilated);

        Assert.False(result.Passed);
        Assert.Contains("Products", result.Requirements[4].Detail.Describe());
    }

    private static SimulationMetrics Outage(ArchitectureBlueprint blueprint)
    {
        var graph = ChallengeHarness.Graph(blueprint);
        var products = graph.Nodes.Single(n => n.Name == "Products");

        return new SimulationEngine()
            .Run(graph, new SimulationScenario([new Fault(ElementType.Node, products.Id, FaultKinds.Down)]))
            .Metrics;
    }
}
