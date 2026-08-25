using arch_forge_backend.Application.Challenges;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Challenges;

public class BlueprintLayoutTests
{
    [Fact]
    public void Traffic_arrives_at_the_top_and_the_database_is_at_the_bottom()
    {
        var drawing = Draw(ChallengeHarness.Catalog.StarterOf("third-party-outage")!);

        Assert.Equal(0, drawing.Row("Storefront"));
        Assert.Equal(1, drawing.Row("Checkout"));
        Assert.Equal(2, drawing.Row("Payments"));
        Assert.Equal(2, drawing.Row("Db"));
    }

    [Fact]
    public void A_component_reached_from_two_depths_is_drawn_under_the_deeper_one()
    {
        var drawing = Draw(ChallengeHarness.Catalog.ReferenceSolutionOf("million-a-day")!);

        Assert.True(drawing.Row("Db") > drawing.Row("Worker"));
        Assert.True(drawing.Row("Worker") > drawing.Row("Jobs"));
        Assert.True(drawing.Row("Jobs") > drawing.Row("Api"));
    }

    [Theory]
    [MemberData(nameof(EveryBlueprint))]
    public void Every_call_in_an_acyclic_blueprint_points_downwards(string slug, string which)
    {
        var blueprint = Blueprint(slug, which);
        if (!IsAcyclic(blueprint)) return;

        var drawing = Draw(blueprint);

        foreach (var call in blueprint.Connections)
            Assert.True(
                drawing.Row(call.To) > drawing.Row(call.From),
                $"'{slug}' ({which}) draws {call.From} -> {call.To} sideways or upwards.");
    }

    [Fact]
    public void A_ring_is_opened_at_the_component_everything_returns_to()
    {
        var blueprint = ChallengeHarness.Catalog.StarterOf("god-service")!;
        var drawing = Draw(blueprint);

        var upwards = blueprint.Connections
            .Where(call => drawing.Row(call.To) <= drawing.Row(call.From))
            .Select(call => $"{call.From} -> {call.To}")
            .OrderBy(call => call, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["Inventory -> Core", "Payments -> Core"], upwards);
        Assert.Equal(0, drawing.Row("Storefront"));
        Assert.Equal(1, drawing.Row("Core"));
    }

    [Fact]
    public void A_component_nothing_wires_is_drawn_under_the_graph()
    {
        var drawing = Draw(ChallengeHarness.Catalog.StarterOf("god-service")!);

        Assert.True(drawing.Row("Legacy") > drawing.Row("Users"));
    }

    [Fact]
    public void Siblings_keep_the_order_the_file_gives_them()
    {
        var blueprint = ChallengeHarness.Catalog.StarterOf("payments-friday")!;
        var drawing = Draw(blueprint);

        Assert.Equal(drawing.Row("Payments"), drawing.Row("Db"));
        Assert.True(drawing.X("Payments") < drawing.X("Db"));
    }

    [Theory]
    [MemberData(nameof(EveryBlueprint))]
    public void The_same_blueprint_is_always_drawn_the_same_way(string slug, string which)
    {
        var blueprint = Blueprint(slug, which);
        var once = Draw(blueprint);
        var twice = Draw(blueprint);

        foreach (var node in blueprint.Nodes)
        {
            Assert.Equal(once.X(node.Name), twice.X(node.Name));
            Assert.Equal(once.Row(node.Name), twice.Row(node.Name));
        }
    }

    [Theory]
    [MemberData(nameof(EveryBlueprint))]
    public void No_two_components_share_a_spot(string slug, string which)
    {
        var architecture = Blueprint(slug, which).Instantiate("test");
        var spots = architecture.Nodes.Select(node => (node.PositionX, node.PositionY)).ToList();

        Assert.Equal(spots.Count, spots.Distinct().Count());
    }

    public static TheoryData<string, string> EveryBlueprint()
    {
        var data = new TheoryData<string, string>();

        foreach (var definition in ChallengeHarness.Catalog.All)
        {
            data.Add(definition.Slug, "starter");
            data.Add(definition.Slug, "solution");
        }

        return data;
    }


    private static ArchitectureBlueprint Blueprint(string slug, string which) =>
        which == "starter"
            ? ChallengeHarness.Catalog.StarterOf(slug)!
            : ChallengeHarness.Catalog.ReferenceSolutionOf(slug)!;

    private static Drawing Draw(ArchitectureBlueprint blueprint) => new(blueprint.Instantiate("test"));

    private sealed class Drawing(Architecture architecture)
    {
        private readonly Dictionary<string, Node> _byName =
            architecture.Nodes.ToDictionary(node => node.Name, StringComparer.Ordinal);

        private readonly List<double> _rows =
            architecture.Nodes.Select(node => node.PositionY).Distinct().OrderBy(y => y).ToList();

        public int Row(string name) => _rows.IndexOf(_byName[name].PositionY);

        public double X(string name) => _byName[name].PositionX;
    }

    private static bool IsAcyclic(ArchitectureBlueprint blueprint)
    {
        var callees = blueprint.Nodes.ToDictionary(node => node.Name, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var call in blueprint.Connections)
            callees[call.From].Add(call.To);

        var settled = new HashSet<string>(StringComparer.Ordinal);
        var walking = new HashSet<string>(StringComparer.Ordinal);

        bool Walk(string name)
        {
            if (walking.Contains(name)) return false;
            if (!settled.Add(name)) return true;

            walking.Add(name);
            var ok = callees[name].All(Walk);
            walking.Remove(name);

            return ok;
        }

        return blueprint.Nodes.All(node => Walk(node.Name));
    }
}
