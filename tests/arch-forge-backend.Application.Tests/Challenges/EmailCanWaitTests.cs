using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Challenges;
using bondstone_backend.Application.Tests.TestDoubles;
using bondstone_backend.Domain.Enums;
using Xunit;

namespace bondstone_backend.Application.Tests.Challenges;

public class EmailCanWaitTests
{
    private const string Slug = "email-can-wait";

    // The solution is what the app hands over when somebody gives up, and it used to be the one
    // drawing in the catalogue with a call that does not wait and nowhere to wait — over Amqp, with
    // no queue, which PROTOCOL_MISMATCH already flagged from the «fix this first» band.
    [Fact]
    public void The_solution_puts_the_email_behind_a_queue_and_carries_no_finding_of_its_own()
    {
        var solution = ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!;
        var findings = ChallengeHarness.Analyze(solution).Findings;

        Assert.Contains(solution.Nodes, node => node.Type == NodeType.Queue);
        Assert.DoesNotContain(findings, f =>
            f.Severity != Severity.Good
            && f.Code is RuleCodes.AsyncNoQueue or RuleCodes.QueueNoDlq or RuleCodes.QueueNoConsumer);
    }

    // Unticking the box is still the whole challenge — the hints say so — and what it leaves behind
    // is the optional goal, not a failed requirement: the warning is the bridge to the queue challenges.
    [Fact]
    public void Unticking_the_box_passes_the_challenge_and_leaves_the_next_lesson_as_the_optional_goal()
    {
        var flipped = Flipped(ChallengeHarness.Catalog.StarterOf(Slug)!);
        var result = ChallengeHarness.Evaluate(Slug, flipped);

        Assert.True(
            result.Passed,
            string.Join(" · ", result.Requirements.Where(r => !r.Met).Select(r => $"{r.Label.Key()} — {r.Detail.Describe()}")));

        var goal = Assert.Single(result.OptionalGoals, g => g.Label.Key() == "challenge.email-can-wait.goal.2");
        Assert.False(goal.Met);
        Assert.Contains(ChallengeHarness.Analyze(flipped).Findings, f => f.Code == RuleCodes.AsyncNoQueue);
    }

    [Fact]
    public void The_call_that_does_not_wait_may_go_through_the_queue_instead_of_straight()
    {
        var result = ChallengeHarness.Evaluate(Slug, ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!);

        Assert.True(result.Requirements[1].Met, result.Requirements[1].Detail.Describe());
        Assert.All(result.OptionalGoals, goal => Assert.True(goal.Met, goal.Label.Key()));
    }

    private static ArchitectureBlueprint Flipped(ArchitectureBlueprint blueprint) => new()
    {
        Name = blueprint.Name,
        Nodes = blueprint.Nodes,
        Connections = blueprint.Connections
            .Select(c => new BlueprintConnection
            {
                From = c.From,
                To = c.To,
                Synchronous = c is { From: "Orders", To: "Mailer" } ? false : c.Synchronous,
                Configuration = c.Configuration
            })
            .ToList()
    };
}
