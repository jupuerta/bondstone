using System.Text.Json;
using bondstone_backend.Application.Challenges;
using bondstone_backend.Application.Tests.TestDoubles;
using bondstone_backend.Domain.Enums;
using Xunit;

namespace bondstone_backend.Application.Tests.Challenges;

// The shortcut that needs no shortcut: drag a component out of the palette, connect it to nothing,
// and see what the catalogue gives you for the drawing. Measured on 22-09 over the 23 starters and
// every node type — «who-is-calling» was passed outright by a loose Client, which displaced the real
// roots from the frontier and left UNAUTHENTICATED_ENTRY, NO_RATE_LIMIT and EXPOSED_DATABASE with
// nothing to look at; five more requirements across three starters turned green because
// `containsNode` counted boxes rather than components, and two more did once the call was deleted
// instead of protected. Both are closed, and this file is the sweep
// itself, so the day a new challenge or a new rule reopens either, it says so here.
public class LooseComponentTests
{
    public static TheoryData<string> Slugs()
    {
        var data = new TheoryData<string>();
        foreach (var challenge in ChallengeHarness.Catalog.All)
            data.Add(challenge.Slug);
        return data;
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public void No_challenge_is_passed_by_a_component_connected_to_nothing(string slug)
    {
        var starter = ChallengeHarness.Catalog.StarterOf(slug)!;

        foreach (var type in Enum.GetValues<NodeType>())
        {
            var one = ChallengeHarness.Evaluate(slug, Loose(starter, "{}", type));
            var two = ChallengeHarness.Evaluate(slug, Loose(starter, """{"zone":"z"}""", type, type));

            Assert.False(one.Passed, $"'{slug}' is passed by its starter plus a loose {type}.");
            Assert.False(two.Passed, $"'{slug}' is passed by its starter plus two loose {type} in a zone.");
        }
    }

    // The sweep above only asks whether the whole challenge falls. This asks the narrower question
    // that made six of them leak: does any single requirement turn green for a box nothing calls?
    [Theory]
    [MemberData(nameof(Slugs))]
    public void No_requirement_turns_green_for_a_component_connected_to_nothing(string slug)
    {
        var starter = ChallengeHarness.Catalog.StarterOf(slug)!;
        var met = Met(ChallengeHarness.Evaluate(slug, starter));

        foreach (var type in Enum.GetValues<NodeType>())
        foreach (var configuration in new[] { "{}", """{"zone":"a"}""", """{"zone":"b"}""" })
        {
            var gained = Met(ChallengeHarness.Evaluate(slug, Loose(starter, configuration, type))).Except(met).ToList();

            Assert.True(
                gained.Count == 0,
                $"'{slug}' gives {string.Join(", ", gained)} for a loose {type} configured {configuration}.");
        }
    }

    // A loose Client used to be taken for the origin of the traffic, and a frontier that starts at a
    // box with no calls out of it is empty: every door in the drawing went unasked.
    [Fact]
    public void A_client_that_calls_nobody_does_not_silence_the_rules_that_read_the_doors()
    {
        foreach (var slug in ChallengeHarness.Catalog.All.Select(c => c.Slug))
        {
            var starter = ChallengeHarness.Catalog.StarterOf(slug)!;

            Assert.Equal(Codes(starter), Codes(Loose(starter, "{}", NodeType.Client)));
        }
    }

    // ORPHAN_NODE is what the board is supposed to say about a loose box, and one starter draws one
    // of its own, so it is the one code this comparison leaves out on both sides.
    private static List<string> Codes(ArchitectureBlueprint board) =>
        ChallengeHarness.Analyze(board).Findings
            .Where(f => f.Severity != Application.Analysis.Severity.Good)
            .Where(f => f.Code != Application.Analysis.RuleCodes.OrphanNode)
            .Select(f => f.Code)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

    private static HashSet<string> Met(ChallengeResult result) =>
        result.Requirements.Where(r => r.Met).Select(r => r.Label.Key()).ToHashSet(StringComparer.Ordinal);

    private static ArchitectureBlueprint Loose(ArchitectureBlueprint blueprint, string configuration, params NodeType[] types)
    {
        var copy = new ArchitectureBlueprint
        {
            Name = blueprint.Name,
            Nodes = blueprint.Nodes
                .Select(n => new BlueprintNode { Name = n.Name, Type = n.Type, X = n.X, Y = n.Y, Configuration = n.Configuration })
                .ToList(),
            Connections = blueprint.Connections
                .Select(c => new BlueprintConnection { From = c.From, To = c.To, Synchronous = c.Synchronous, Configuration = c.Configuration })
                .ToList()
        };

        var drawn = 0;
        foreach (var type in types)
            copy.Nodes.Add(new BlueprintNode
            {
                Name = $"Loose {type} {++drawn}",
                Type = type,
                X = 1200,
                Y = 200 * drawn,
                Configuration = JsonDocument.Parse(configuration).RootElement.Clone()
            });

        return copy;
    }
}
