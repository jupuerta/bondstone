using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Challenges;
using Xunit;

namespace arch_forge_backend.Application.Tests.Challenges;

/// <summary>
/// V4 D3.5, and the reason a challenge ships with a solution: <strong>this is the regression net
/// for the whole product</strong>. A rule landing in VS3, a constant moving in VS2 or a change in
/// how a mechanism composes will show up here as a challenge that stopped being passable - which is
/// something CI can say and a user cannot.
///
/// The two assertions that matter are a pair: the reference solution passes, and the starter does
/// not. One without the other proves nothing - a requirement everybody meets is decoration.
/// </summary>
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
        // Loading is validating (D1.5): an unknown kind, a metric that is not in the catalogue or a
        // starter wiring an edge to a node nobody declared all throw here rather than at run time.
        Assert.NotEmpty(ChallengeHarness.Catalog.All);
    }

    // ---- V17 D1. The one operand that travels -----------------------------------------------

    [Fact]
    public void Only_a_latency_ceiling_over_an_empty_scenario_publishes_its_budget()
    {
        // The four conditions of ChallengeRequirementDto.LatencyBudgetMs, read off the real
        // catalogue rather than a hand-built requirement: what this has to be true about is the
        // thirteen definitions that ship, not a fixture that agrees with the mapper.
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
        // `retry-storm` and `third-party-outage` both cap `faultedLatencyMs` - under a fault. The
        // cascade on screen is the walk of whatever scenario the user has set, and the client has
        // no way to know it is the same one: a challenge's faults point at components by name and
        // the canvas holds ids. So the line is not drawn, rather than drawn against the wrong walk.
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
        // D3.6 is why the checklist is a kind and a label: the client renders it and never gets
        // close to evaluating it. The budget is the single exception because it is drawn and not
        // decided, so this is the test that keeps the exception single.
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

        // D3.3: a bare ❌ is not the feature. Whatever is not met has to say why, with a number in it.
        Assert.All(
            result.Requirements.Concat(result.OptionalGoals).Where(r => !r.Met),
            requirement => Assert.NotEmpty(requirement.Detail.Key()));
    }

    /// <summary>
    /// D3.5, closing the loop: what the endpoint hands to whoever passes is the very architecture
    /// this suite verifies on every build. Nobody is ever shown a "reference solution" that does not
    /// solve the thing.
    /// </summary>
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

        // Every instantiation mints fresh ids, so running twice is also a check that no verdict
        // depends on a Guid - which is what makes a shared link show the next person what it
        // showed you (V3 D1.2).
        var first = ChallengeHarness.Evaluate(slug, blueprint);
        var second = ChallengeHarness.Evaluate(slug, blueprint);

        Assert.Equal(
            first.Requirements.Select(r => (r.Kind, r.Met, r.Detail.Describe())),
            second.Requirements.Select(r => (r.Kind, r.Met, r.Detail.Describe())));
    }
}

/// <summary>
/// V8 §6.5. The catalogue as a catalogue rather than as twelve separate files: the itinerary is
/// content too, and with four challenges nobody would have noticed it drifting.
/// </summary>
public class ChallengeCatalogueShapeTests
{
    [Fact]
    public void Every_challenge_is_in_one_of_the_three_bands()
    {
        // The catalogue refuses to load anything else, so this is the assertion that says out loud
        // what that refusal buys: `difficulty` used to be a free string and a typo invented a band.
        Assert.All(ChallengeHarness.Catalog.All, c => Assert.True(Difficulties.IsKnown(c.Difficulty), c.Slug));
    }

    [Fact]
    public void The_catalogue_reads_as_a_ramp()
    {
        var ranks = ChallengeHarness.Catalog.All.Select(c => Difficulties.Rank(c.Difficulty)).ToList();

        Assert.Equal(ranks.OrderBy(r => r), ranks);

        // And it starts on the ground: the hole V8 opened this work to close was that the easiest
        // challenge already assumed the queueing factor.
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
        // V7 D1.1 made the definition point at prose instead of holding it, and the convention is
        // what keeps the two from drifting: `challenge.{slug}.…`, checked here so a copied file
        // that forgot to rename its keys fails on this side rather than rendering another
        // challenge's sentences.
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
        // D3.6: free and unlimited, and always the same three steps - where to look, which
        // principle, which mechanism. Two would skip one of them.
        Assert.Equal(3, ChallengeHarness.Catalog.Find(slug)!.HintKeys.Count);
    }
}

/// <summary>
/// The content of `payments-friday` itself: the numbers of the starter and of the solution, which
/// are what the challenge teaches. Written from the model, not from the output.
/// </summary>
public class PaymentsFridayTests
{
    private const string Slug = "payments-friday";

    [Fact]
    public void The_starter_falls_apart_when_Payments_goes_down()
    {
        var result = ChallengeHarness.Evaluate(Slug, ChallengeHarness.Catalog.StarterOf(Slug)!);
        var survives = result.Requirements[0];

        Assert.False(survives.Met);

        // Orders → Payments has no timeout, so no mechanism of that edge can act (V3 D4.2): Orders
        // waits forever, and Storefront - which does have a timeout but no alternative - goes with
        // it. One entry point, and it is affected.
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

        // Exactly one of the two calls into a Service is unprotected, and the message says which.
        Assert.Contains("Orders", timeouts.Detail.Arg("calls"));
        Assert.Contains("Payments", timeouts.Detail.Arg("calls"));
        Assert.Single(timeouts.Elements);
    }

    [Fact]
    public void The_solution_contains_the_fault_at_Orders()
    {
        var result = ChallengeHarness.Evaluate(Slug, ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!);

        Assert.True(result.Passed);

        // And the optional goal too: the same fallback that holds Payments holds the database.
        Assert.All(result.OptionalGoals, goal => Assert.True(goal.Met));
    }

    [Fact]
    public void Deleting_Payments_does_not_pass_the_challenge()
    {
        // The anti-cheese of D2.3, and it costs nothing: faults point at components by name, so
        // removing the component removes the scenario instead of surviving it.
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
