using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Challenges;
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
