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

    [Theory]
    [MemberData(nameof(Slugs))]
    public void The_reference_solution_passes(string slug)
    {
        var result = ChallengeHarness.Evaluate(slug, ChallengeHarness.Catalog.ReferenceSolutionOf(slug)!);

        Assert.True(
            result.Passed,
            $"'{slug}' is not passed by its own reference solution: " +
            string.Join(" · ", result.Requirements.Where(r => !r.Met).Select(r => $"{r.Label} — {r.Detail}")));
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
            requirement => Assert.False(string.IsNullOrWhiteSpace(requirement.Detail)));
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
            first.Requirements.Select(r => (r.Kind, r.Met, r.Detail)),
            second.Requirements.Select(r => (r.Kind, r.Met, r.Detail)));
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
        Assert.Contains("1", survives.Detail);
        Assert.NotEmpty(survives.Elements);
    }

    [Fact]
    public void The_starter_is_missing_the_timeout_that_makes_everything_else_possible()
    {
        var result = ChallengeHarness.Evaluate(Slug, ChallengeHarness.Catalog.StarterOf(Slug)!);
        var timeouts = result.Requirements[1];

        Assert.False(timeouts.Met);
        Assert.Contains("timeout", timeouts.Detail);

        // Exactly one of the two calls into a Service is unprotected, and the message says which.
        Assert.Contains("Orders", timeouts.Detail);
        Assert.Contains("Payments", timeouts.Detail);
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
        Assert.Contains("Payments", survives.Detail);
    }
}
