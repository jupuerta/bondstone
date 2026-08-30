using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis;

public class AnalysisContractTests
{
    private static readonly string[] References =
        ["minimal-api-db", "resilient-async", "gateway-microservices", "worst-case"];


    [Fact]
    public void Coverage_names_every_registered_rule_exactly_once()
    {
        foreach (var name in References)
        {
            var coverage = ReferenceArchitecture.Analyze(name).Coverage;

            Assert.Equal(26, coverage.Count);
            Assert.Equal(26, coverage.Select(rule => rule.Code).Distinct().Count());
        }
    }

    // V22 §11. The panel hoists these above the score, so which rules they are has to travel — a copy
    // of the list on the frontend would be one more table to drift, which is exactly how the area
    // column of the challenge catalogue was wrong for four rules.
    [Fact]
    public void Coverage_names_the_two_rules_that_talk_about_the_drawing()
    {
        foreach (var name in References)
        {
            var coverage = ReferenceArchitecture.Analyze(name).Coverage;

            Assert.Equal(
                [RuleCodes.ImplausibleDependency, RuleCodes.ProtocolMismatch],
                coverage.Where(rule => rule.DescribesTheDrawing).Select(rule => rule.Code).Order());
        }
    }

    // And the reason they are marked instead of scored apart: a finding about the drawing costs the
    // same as any other of its severity, and lives in the area whose consequences it has. The mark is
    // about reading order, not about points (V21 §3 measured the alternative).
    [Fact]
    public void The_drawing_rules_score_where_their_consequences_are()
    {
        var rules = new IArchitectureRule[] { new ProtocolMismatchRule(), new ImplausibleDependencyRule() };

        Assert.All(rules, rule => Assert.Equal(Area.Resilience, rule.Area));
        Assert.All(rules, rule => Assert.True(rule.DescribesTheDrawing));
    }

    [Fact]
    public void Coverage_is_ordered_by_area_and_then_by_code()
    {
        var coverage = ReferenceArchitecture.Analyze("worst-case").Coverage;

        Assert.Equal(
            coverage.OrderBy(rule => rule.Area).ThenBy(rule => rule.Code, StringComparer.Ordinal).ToList(),
            coverage);
    }

    [Fact]
    public void Coverage_says_the_same_thing_as_the_findings_beside_it()
    {
        foreach (var name in References)
        {
            var result = ReferenceArchitecture.Analyze(name);

            foreach (var rule in result.Coverage)
            {
                var findings = result.Findings.Where(f => f.Code == rule.Code).ToList();

                switch (rule.State)
                {
                    case RuleState.Fired:
                        Assert.NotEmpty(findings);
                        Assert.DoesNotContain(findings, f => f.Severity == Severity.Good);
                        break;

                    case RuleState.Passed:
                        Assert.Equal(Severity.Good, Assert.Single(findings).Severity);
                        break;

                    case RuleState.NotApplicable:
                        Assert.Empty(findings);
                        break;
                }

                Assert.All(findings, finding => Assert.Equal(rule.Area, finding.Area));
            }
        }
    }

    [Fact]
    public void Coverage_counts_the_passed_of_the_summary()
    {
        foreach (var name in References)
        {
            var result = ReferenceArchitecture.Analyze(name);

            Assert.Equal(
                result.Summary.Passed,
                result.Coverage.Count(rule => rule.State == RuleState.Passed));
        }
    }

    [Fact]
    public void Coverage_separates_the_rule_that_had_nothing_to_look_at_from_the_one_that_passed()
    {
        var result = ReferenceArchitecture.Analyze("minimal-api-db");

        Assert.Equal(RuleState.NotApplicable, StateOf(result, RuleCodes.UndersizedComponent));
        Assert.Equal(RuleState.NotApplicable, StateOf(result, RuleCodes.QueueNoDlq));
        Assert.Equal(RuleState.Passed, StateOf(result, RuleCodes.Spof));
        Assert.Equal(RuleState.Fired, StateOf(result, RuleCodes.NoObservability));
    }

    // The only reference with a call that does not wait is the one whose calls all go through the
    // queue: it passes, and the other three have nothing to look at. That is what keeps the four
    // scores where they were.
    [Fact]
    public void Coverage_hears_the_queue_rule_only_where_something_fires_without_waiting()
    {
        Assert.Equal(RuleState.Passed, StateOf(ReferenceArchitecture.Analyze("resilient-async"), RuleCodes.AsyncNoQueue));

        foreach (var name in References.Where(name => name != "resilient-async"))
            Assert.Equal(RuleState.NotApplicable, StateOf(ReferenceArchitecture.Analyze(name), RuleCodes.AsyncNoQueue));
    }

    [Fact]
    public void Coverage_moves_with_the_drawing()
    {
        var withoutQueue = ReferenceArchitecture.Analyze(new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 3, protocol: ProtocolType.Sql)
            .Build());

        var withQueue = ReferenceArchitecture.Analyze(new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Cola", NodeType.Queue).Node("W", NodeType.Worker)
            .Connect("Api", "Cola", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Cola", "W", synchronous: false, protocol: ProtocolType.Amqp)
            .Build());

        Assert.Equal(RuleState.NotApplicable, StateOf(withoutQueue, RuleCodes.QueueNoDlq));
        Assert.Equal(RuleState.Fired, StateOf(withQueue, RuleCodes.QueueNoDlq));
    }


    [Fact]
    public void Every_rule_that_stood_down_says_why_and_says_it_in_its_own_name()
    {
        foreach (var name in References)
        {
            foreach (var rule in ReferenceArchitecture.Analyze(name).Coverage)
            {
                if (rule.State != RuleState.NotApplicable)
                {
                    Assert.Null(rule.Reason);
                    continue;
                }

                Assert.NotNull(rule.Reason);
                Assert.StartsWith($"rule.na.{rule.Code}.", rule.Reason!.Key, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_empty_canvas_gets_twenty_six_different_sentences()
    {
        var coverage = ReferenceArchitecture.Analyze(new GraphBuilder().Build()).Coverage;

        Assert.All(coverage, rule => Assert.Equal(RuleState.NotApplicable, rule.State));
        Assert.Equal(26, coverage.Select(rule => rule.Reason!.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_rule_that_stands_down_twice_for_different_reasons_says_two_different_things()
    {
        var idle = ReferenceArchitecture.Analyze(new GraphBuilder()
            .Node("Users", NodeType.Client).Node("Api", NodeType.Api)
            .Connect("Users", "Api")
            .Build());

        var quiet = ReferenceArchitecture.Analyze(new GraphBuilder()
            .Node("Users", NodeType.Client, "{\"requestsPerSecond\":10}").Node("Api", NodeType.Api)
            .Connect("Users", "Api")
            .Build());

        Assert.Equal("rule.na.MISSING_CDN.noLoad", ReasonOf(idle, RuleCodes.MissingCdn).Key);
        Assert.Equal("rule.na.MISSING_CDN.lowTraffic", ReasonOf(quiet, RuleCodes.MissingCdn).Key);

        Assert.Equal(
            RuleThresholds.CdnWorthTraffic,
            ReasonOf(quiet, RuleCodes.MissingCdn).Args!["traffic"]);
    }

    [Fact]
    public void The_reason_goes_away_the_moment_the_drawing_answers_it()
    {
        var withQueue = ReferenceArchitecture.Analyze(new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Cola", NodeType.Queue).Node("W", NodeType.Worker)
            .Connect("Api", "Cola", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Cola", "W", synchronous: false, protocol: ProtocolType.Amqp)
            .Build());

        Assert.Equal(RuleState.Fired, StateOf(withQueue, RuleCodes.QueueNoDlq));
        Assert.Null(withQueue.Coverage.Single(rule => rule.Code == RuleCodes.QueueNoDlq).Reason);
    }


    [Fact]
    public void Load_says_nothing_when_nobody_declared_traffic()
    {
        foreach (var name in References)
            Assert.Empty(ReferenceArchitecture.Analyze(name).Load);
    }

    [Fact]
    public void Load_publishes_the_utilisation_the_rule_grades_on()
    {
        var graph = new GraphBuilder()
            .Node("Users", NodeType.Client, "{\"requestsPerSecond\":340}")
            .Node("Api", NodeType.Api, "{\"authentication\":true}")
            .Node("Db", NodeType.Database)
            .Connect("Users", "Api")
            .Connect("Api", "Db", timeoutSeconds: 3, protocol: ProtocolType.Sql)
            .Build();

        var result = ReferenceArchitecture.Analyze(graph);

        var api = LoadOf(result, graph, "Api");
        Assert.Equal(340m, api.Rps);
        Assert.Equal(CapacityModel.Api, api.CapacityRps);
        Assert.Equal(0.68m, api.Utilization);
        Assert.DoesNotContain(result.Findings, f =>
            f.Code == RuleCodes.UndersizedComponent && f.Elements.Any(e => e.Id == graph.NodeId("Api")));

        var db = LoadOf(result, graph, "Db");
        Assert.Equal(340m, db.Rps);
        Assert.Equal(CapacityModel.Database, db.CapacityRps);
        Assert.Equal(1.7m, db.Utilization);

        var finding = result.Findings.Single(f => f.Code == RuleCodes.UndersizedComponent);
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal(graph.NodeId("Db"), finding.Elements[0].Id);
    }

    [Fact]
    public void Load_covers_every_component_including_the_ones_nobody_can_size()
    {
        var graph = new GraphBuilder()
            .Node("Users", NodeType.Client, "{\"requestsPerSecond\":340}")
            .Node("Api", NodeType.Api)
            .Node("Obs", NodeType.Observability)
            .Connect("Users", "Api")
            .Connect("Obs", "Api", timeoutSeconds: 5, fallback: true)
            .Build();

        var result = ReferenceArchitecture.Analyze(graph);

        Assert.Equal(graph.Nodes.Select(n => n.Id), result.Load.Select(entry => entry.NodeId));

        var users = LoadOf(result, graph, "Users");
        Assert.Equal(CapacityModel.Client, users.CapacityRps);
        Assert.Equal(0m, users.Utilization);
    }

    [Fact]
    public void Load_reads_the_instances_declared_on_the_component()
    {
        var graph = new GraphBuilder()
            .Node("Users", NodeType.Client, "{\"requestsPerSecond\":900}")
            .Node("Api", NodeType.Api, "{\"authentication\":true,\"instances\":3}")
            .Connect("Users", "Api")
            .Build();

        var api = LoadOf(ReferenceArchitecture.Analyze(graph), graph, "Api");

        Assert.Equal(CapacityModel.Api * 3, api.CapacityRps);
        Assert.Equal(0.6m, api.Utilization);
    }

    [Fact]
    public void Load_reports_what_got_in_and_not_what_wanted_in()
    {
        var graph = new GraphBuilder()
            .Node("Users", NodeType.Client, "{\"requestsPerSecond\":4000}")
            .Node("Api", NodeType.Api, "{\"authentication\":true,\"rateLimit\":100}")
            .Connect("Users", "Api")
            .Build();

        var api = LoadOf(ReferenceArchitecture.Analyze(graph), graph, "Api");

        Assert.Equal(100m, api.Rps);
        Assert.Equal(0.2m, api.Utilization);
    }

    [Fact]
    public void Load_and_the_simulator_report_the_same_component_at_the_same_percentage()
    {
        var graph = new GraphBuilder()
            .Node("Users", NodeType.Client, "{\"requestsPerSecond\":340}")
            .Node("Api", NodeType.Api, "{\"authentication\":true}")
            .Node("Db", NodeType.Database)
            .Connect("Users", "Api")
            .Connect("Api", "Db", timeoutSeconds: 3, protocol: ProtocolType.Sql)
            .Build();

        var analysis = ReferenceArchitecture.Analyze(graph);
        var simulation = new SimulationEngine().Run(graph, SimulationScenario.Empty);

        foreach (var entry in analysis.Load)
        {
            var state = simulation.States.Single(s => s.Type == ElementType.Node && s.Id == entry.NodeId);
            Assert.Equal(state.Utilization, entry.Utilization);
        }
    }


    [Fact]
    public void CallLoad_says_nothing_when_nobody_declared_traffic()
    {
        foreach (var name in References)
            Assert.Empty(ReferenceArchitecture.Analyze(name).CallLoad);
    }

    [Fact]
    public void CallLoad_is_where_the_amplification_the_panel_reports_actually_happens()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, "{\"requestsPerSecond\":300,\"authentication\":true}")
            .Node("A", NodeType.Service)
            .Node("B", NodeType.Service)
            .Node("Db", NodeType.Database, "{\"replicas\":2}")
            .Connect("Api", "A", timeoutSeconds: 3)
            .Connect("Api", "B", timeoutSeconds: 3)
            .Connect("A", "Db", timeoutSeconds: 3, protocol: ProtocolType.Sql)
            .Connect("B", "Db", timeoutSeconds: 3, protocol: ProtocolType.Sql)
            .Build();

        var result = ReferenceArchitecture.Analyze(graph);

        Assert.Equal(300m, CallLoadOf(result, graph, "Api", "A"));
        Assert.Equal(300m, CallLoadOf(result, graph, "Api", "B"));
        Assert.Equal(300m, CallLoadOf(result, graph, "A", "Db"));
        Assert.Equal(300m, CallLoadOf(result, graph, "B", "Db"));

        Assert.Equal(600m, result.Load.Single(entry => entry.NodeId == graph.NodeId("Db")).Rps);
    }

    [Fact]
    public void CallLoad_carries_the_split_the_source_makes_and_not_the_load_it_received()
    {
        var graph = new GraphBuilder()
            .Node("Users", NodeType.Client, "{\"requestsPerSecond\":1000}")
            .Node("Gw", NodeType.Gateway, "{\"authentication\":true}")
            .Node("A", NodeType.Service)
            .Node("B", NodeType.Service)
            .Node("Cache", NodeType.Cache)
            .Node("Db", NodeType.Database, "{\"replicas\":2}")
            .Connect("Users", "Gw")
            .Connect("Gw", "A", timeoutSeconds: 3)
            .Connect("Gw", "B", timeoutSeconds: 3)
            .Connect("A", "Cache", timeoutSeconds: 3)
            .Connect("B", "Cache", timeoutSeconds: 3)
            .Connect("Cache", "Db", timeoutSeconds: 3, protocol: ProtocolType.Sql)
            .Build();

        var result = ReferenceArchitecture.Analyze(graph);

        Assert.Equal(500m, CallLoadOf(result, graph, "Gw", "A"));
        Assert.Equal(500m, CallLoadOf(result, graph, "Gw", "B"));

        Assert.Equal(200m, CallLoadOf(result, graph, "Cache", "Db"));
    }

    [Fact]
    public void CallLoad_lists_every_call_including_the_ones_that_carry_nothing()
    {
        var graph = new GraphBuilder()
            .Node("Users", NodeType.Client, "{\"requestsPerSecond\":100}")
            .Node("Api", NodeType.Api, "{\"authentication\":true}")
            .Node("Obs", NodeType.Observability)
            .Connect("Users", "Api")
            .Connect("Obs", "Api", timeoutSeconds: 5, fallback: true)
            .Build();

        var result = ReferenceArchitecture.Analyze(graph);

        Assert.Equal(graph.Connections.Select(c => c.Id), result.CallLoad.Select(entry => entry.ConnectionId));
        Assert.Equal(100m, CallLoadOf(result, graph, "Users", "Api"));
        Assert.Equal(0m, CallLoadOf(result, graph, "Obs", "Api"));
    }


    [Fact]
    public void Cost_travels_with_the_analysis_and_says_it_even_when_nobody_declared_traffic()
    {
        foreach (var name in References)
        {
            var result = ReferenceArchitecture.Analyze(name);

            Assert.NotEmpty(result.Cost.Components);
            Assert.Equal(result.Cost.Components.Sum(component => component.Total), result.Cost.Total);
        }
    }

    [Fact]
    public void Cost_lists_every_component_including_the_ones_that_are_not_yours_to_pay_for()
    {
        var graph = new GraphBuilder()
            .Node("Users", NodeType.Client, "{\"requestsPerSecond\":40}")
            .Node("Api", NodeType.Api, "{\"authentication\":true,\"instances\":2}")
            .Node("Pagos", NodeType.ExternalService)
            .Node("Db", NodeType.Database, "{\"readReplicas\":1}")
            .Connect("Users", "Api")
            .Connect("Api", "Pagos", timeoutSeconds: 3, fallback: true)
            .Connect("Api", "Db", timeoutSeconds: 3, protocol: ProtocolType.Sql)
            .Build();

        var cost = ReferenceArchitecture.Analyze(graph).Cost;

        Assert.Equal(graph.Nodes.Select(n => n.Id), cost.Components.Select(component => component.NodeId));

        Assert.Equal(0, CostOf(cost, graph, "Users"));
        Assert.Equal(0, CostOf(cost, graph, "Pagos"));
        Assert.Equal(CostModel.Api * 2, CostOf(cost, graph, "Api"));
        Assert.Equal(CostModel.Database * 2, CostOf(cost, graph, "Db"));

        Assert.Equal(CostModel.Of(graph), cost.Total);
    }

    [Fact]
    public void Cost_publishes_the_two_factors_the_screen_multiplies()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, "{\"requestsPerSecond\":40,\"instances\":3}")
            .Build();

        var api = ComponentOf(ReferenceArchitecture.Analyze(graph).Cost, graph, "Api");

        Assert.Equal(CostModel.Api, api.PerInstance);
        Assert.Equal(3, api.Instances);
        Assert.Equal(CostModel.Api * 3, api.Total);

        // The panel has never known the names of the nodes: they reach it only inside the backend's
        // sentences. Without this the breakdown is a column of anonymous bars.
        Assert.Equal("Api", api.Name);
    }

    [Fact]
    public void Spreading_a_component_over_zones_does_not_change_what_it_costs()
    {
        var spread = new GraphBuilder()
            .Node("Api a", NodeType.Api, "{\"zone\":\"a\"}")
            .Node("Api b", NodeType.Api, "{\"zone\":\"b\"}")
            .Node("Api c", NodeType.Api, "{\"zone\":\"c\"}")
            .Build();

        var stacked = new GraphBuilder()
            .Node("Api", NodeType.Api, "{\"zone\":\"a\",\"instances\":3}")
            .Build();

        Assert.Equal(12, ReferenceArchitecture.Analyze(spread).Cost.Total);
        Assert.Equal(12, ReferenceArchitecture.Analyze(stacked).Cost.Total);
    }


    private static int CostOf(ArchitectureCost cost, ArchitectureGraph graph, string name) =>
        ComponentOf(cost, graph, name).Total;

    private static ComponentCost ComponentOf(ArchitectureCost cost, ArchitectureGraph graph, string name) =>
        cost.Components.Single(component => component.NodeId == graph.NodeId(name));

    private static LocalizedText ReasonOf(AnalysisResult result, string code) =>
        result.Coverage.Single(rule => rule.Code == code).Reason!;

    private static RuleState StateOf(AnalysisResult result, string code) =>
        result.Coverage.Single(rule => rule.Code == code).State;

    private static ComponentLoad LoadOf(AnalysisResult result, ArchitectureGraph graph, string name) =>
        result.Load.Single(entry => entry.NodeId == graph.NodeId(name));

    private static decimal CallLoadOf(AnalysisResult result, ArchitectureGraph graph, string source, string target) =>
        result.CallLoad.Single(entry => entry.ConnectionId == graph.EdgeId(source, target)).Rps;
}
