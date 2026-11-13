using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Analysis.Rules;
using bondstone_backend.Application.Simulation;
using bondstone_backend.Application.Tests.Analysis.Fixtures;
using bondstone_backend.Application.Tests.Analysis.Rules.Support;
using bondstone_backend.Application.Tests.TestDoubles;
using bondstone_backend.Domain.Enums;
using Xunit;

namespace bondstone_backend.Application.Tests.Analysis.Rules;

public class OverprovisionedRuleTests
{
    [Fact]
    public void Says_nothing_until_somebody_declares_how_much_traffic_comes_in()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"instances":8}""")
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db")
            .Build();

        var result = new OverprovisionedRule().Evaluate(graph);

        Assert.False(result.IsApplicable);
        Assert.Equal("rule.na.OVERPROVISIONED.noLoad", result.NotApplicableReason!.Key);
    }

    [Fact]
    public void Four_copies_of_a_component_at_three_percent_is_money_burning()
    {
        var graph = Copies(4, rps: 60);

        var finding = Assert.Single(new OverprovisionedRule().Evaluate(graph).Findings);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(graph.Nodes.Single(n => n.Name == "Api").Id, Assert.Single(finding.Elements).Id);
    }

    [Fact]
    public void The_second_copy_is_never_waste_because_it_is_what_takes_the_component_out_of_spof()
    {
        var graph = Copies(2, rps: 10);

        Assert.Empty(new OverprovisionedRule().Evaluate(graph).Findings);
    }

    [Fact]
    public void What_it_advises_dropping_to_never_goes_below_two()
    {
        var finding = Assert.Single(new OverprovisionedRule().Evaluate(Copies(6, rps: 60)).Findings);

        Assert.Equal("2", finding.Explanation.Arg("keep"));
        Assert.Equal("4", finding.Explanation.Arg("spare"));
    }

    [Fact]
    public void What_it_advises_dropping_to_is_what_the_declared_load_still_fits_in()
    {
        var finding = Assert.Single(new OverprovisionedRule().Evaluate(Copies(10, rps: 900)).Findings);

        Assert.Equal("900", finding.Explanation.Arg("loadRps"));
        Assert.Equal("3", finding.Explanation.Arg("keep"));
        Assert.Equal("7", finding.Explanation.Arg("spare"));
    }

    [Fact]
    public void The_finding_says_what_the_copies_that_are_left_over_cost()
    {
        var finding = Assert.Single(new OverprovisionedRule().Evaluate(Copies(6, rps: 60)).Findings);

        Assert.Equal("6", finding.Message.Arg("copies"));

        Assert.Equal((6 * CostModel.Api).ToString(), finding.Explanation.Arg("cost"));
        Assert.Equal((4 * CostModel.Api).ToString(), finding.Explanation.Arg("waste"));
    }

    [Fact]
    public void A_component_that_is_busy_is_not_overprovisioned_however_many_copies_it_has()
    {
        Assert.Empty(new OverprovisionedRule().Evaluate(Copies(4, rps: 900)).Findings);
    }

    [Fact]
    public void Read_replicas_are_copies_here_exactly_as_they_are_capacity_in_the_load_model()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":40}""")
            .Node("Db", NodeType.Database, """{"readReplicas":5}""")
            .Connect("Api", "Db")
            .Build();

        var finding = Assert.Single(new OverprovisionedRule().Evaluate(graph).Findings);

        Assert.Equal(graph.Nodes.Single(n => n.Name == "Db").Id, Assert.Single(finding.Elements).Id);
        Assert.Equal("6", finding.Message.Arg("copies"));
        Assert.Equal("finding.OVERPROVISIONED.suggestion.database", finding.Suggestion.Key());
    }

    [Fact]
    public void Everything_this_rule_can_flag_is_something_you_pay_for()
    {
        foreach (var type in Enum.GetValues<NodeType>())
        {
            if (NodeRoles.IsYoursToSize(type))
                Assert.True(NodeRoles.IsYoursToPayFor(type), $"{type} is sized but not billed");
        }
    }

    [Fact]
    public void An_orphan_is_a_drawing_problem_and_not_a_billing_one()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":40}""")
            .Node("Db", NodeType.Database)
            .Node("Suelto", NodeType.Service, """{"instances":9}""")
            .Connect("Api", "Db")
            .Build();

        var orphan = graph.Nodes.Single(n => n.Name == "Suelto").Id;

        Assert.DoesNotContain(
            new OverprovisionedRule().Evaluate(graph).Findings,
            f => f.Elements.Any(e => e.Id == orphan));
    }

    [Fact]
    public void The_area_it_answers_for_is_the_new_one_and_it_is_the_last_of_the_enum()
    {
        var rule = new OverprovisionedRule();

        Assert.Equal(Area.Cost, rule.Area);
        Assert.Equal(Area.Cost, Enum.GetValues<Area>()[^1]);
    }

    [Fact]
    public void The_four_references_never_reach_it_because_none_of_them_declares_traffic()
    {
        foreach (var name in new[] { "minimal-api-db", "resilient-async", "gateway-microservices", "worst-case" })
        {
            var result = new OverprovisionedRule().Evaluate(ReferenceArchitecture.Graph(name));

            Assert.False(result.IsApplicable);
        }
    }

    private static ArchitectureGraph Copies(int instances, int rps) => new GraphBuilder()
        .Node("Users", NodeType.Client, $$"""{"requestsPerSecond":{{rps}}}""")
        .Node("Api", NodeType.Api, $$"""{"authentication":true,"instances":{{instances}}}""")
        .Connect("Users", "Api")
        .Build();
}
