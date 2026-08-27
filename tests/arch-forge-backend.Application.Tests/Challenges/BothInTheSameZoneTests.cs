using System.Text.Json;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Challenges;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.TestDoubles;
using Xunit;

namespace arch_forge_backend.Application.Tests.Challenges;

public class BothInTheSameZoneTests
{
    private const string Slug = "both-in-the-same-zone";

    [Fact]
    public void The_starter_falls_on_the_zone_and_on_nothing_else()
    {
        var result = ChallengeHarness.Evaluate(Slug, ChallengeHarness.Catalog.StarterOf(Slug)!);
        var survives = result.Requirements[2];

        Assert.False(result.Passed);
        Assert.False(survives.Met);
        Assert.Equal("metric.entryPointsAffected.value(value=1)", survives.Detail.Arg("actual"));

        Assert.True(result.Requirements[0].Met);
        Assert.True(result.Requirements[1].Met);
        Assert.True(result.Requirements[3].Met);
    }

    [Fact]
    public void The_analyzer_names_the_two_copies_and_the_zone_they_share()
    {
        var findings = ChallengeHarness.Analyze(ChallengeHarness.Catalog.StarterOf(Slug)!).Findings;

        var sameZone = Assert.Single(findings, f => f.Message.Key() == "finding.SPOF.sameZone.message");

        Assert.Equal(RuleCodes.Spof, sameZone.Code);
        Assert.Equal(Severity.Warning, sameZone.Severity);
        Assert.Equal("a", sameZone.Message.Arg("zone"));
        Assert.Equal(2, sameZone.Elements.Count);
    }

    [Fact]
    public void Splitting_the_copies_silences_the_finding_and_costs_no_more()
    {
        var starter = ChallengeHarness.Catalog.StarterOf(Slug)!;
        var solution = ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!;

        Assert.DoesNotContain(
            ChallengeHarness.Analyze(solution).Findings,
            f => f.Message.Key() == "finding.SPOF.sameZone.message");

        Assert.Equal(
            CostModel.Of(ChallengeHarness.Graph(starter)),
            CostModel.Of(ChallengeHarness.Graph(solution)));
    }

    [Fact]
    public void Either_zone_the_copies_live_in_can_go_down_and_the_door_stays_open()
    {
        var solution = ChallengeHarness.Catalog.ReferenceSolutionOf(Slug)!;

        foreach (var zone in new[] { "a", "b" })
        {
            var metrics = Losing(solution, zone);

            Assert.Equal(0, metrics.EntryPointsAffected);
            Assert.Equal(1, metrics.ComponentsDown);
        }

        Assert.True(ChallengeHarness.Evaluate(Slug, solution).Passed);
    }

    [Fact]
    public void Emptying_the_zone_is_not_spreading_it()
    {
        var moved = Rezoned(ChallengeHarness.Catalog.StarterOf(Slug)!, _ => "b");

        var result = ChallengeHarness.Evaluate(Slug, moved);

        Assert.Equal(0, Losing(moved, "a").ComponentsDown);
        Assert.True(result.Requirements[2].Met);
        Assert.False(result.Requirements[3].Met);
        Assert.False(result.Passed);
    }

    private static SimulationMetrics Losing(ArchitectureBlueprint blueprint, string zone) =>
        new SimulationEngine()
            .Run(ChallengeHarness.Graph(blueprint), new SimulationScenario([], [zone]))
            .Metrics;

    private static ArchitectureBlueprint Rezoned(ArchitectureBlueprint blueprint, Func<BlueprintNode, string> zone) => new()
    {
        Name = blueprint.Name,
        Nodes = blueprint.Nodes
            .Select(node => new BlueprintNode
            {
                Name = node.Name,
                Type = node.Type,
                X = node.X,
                Y = node.Y,
                Configuration = JsonDocument.Parse(Rewritten(node, zone(node))).RootElement.Clone()
            })
            .ToList(),
        Connections = blueprint.Connections
    };

    private static string Rewritten(BlueprintNode node, string zone)
    {
        var fields = node.Configuration!.Value.EnumerateObject()
            .Where(p => !p.Name.Equals("zone", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{JsonSerializer.Serialize(p.Name)}:{p.Value.GetRawText()}")
            .Append($"\"zone\":{JsonSerializer.Serialize(zone)}");

        return $"{{{string.Join(",", fields)}}}";
    }
}
