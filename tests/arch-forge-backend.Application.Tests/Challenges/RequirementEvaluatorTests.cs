using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Challenges;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Challenges;

public class RequirementEvaluatorTests
{
    private static ArchitectureBlueprint ApiCacheDb() => Blueprint.Of(
        [("Api", NodeType.Api), ("Cache", NodeType.Cache), ("Db", NodeType.Database)],
        [("Api", "Cache", true, """{"timeoutSeconds":2}"""),
         ("Cache", "Db", true, """{"timeoutSeconds":5}""")]);

    private static ChallengeRequirementResult Run(ChallengeRequirement requirement, ArchitectureBlueprint blueprint) =>
        RequirementEvaluator.Evaluate(requirement, ChallengeHarness.Context(blueprint));

    [Fact]
    public void ContainsNode_counts_by_type()
    {
        var blueprint = ApiCacheDb();

        Assert.True(Run(new ChallengeRequirement { Kind = RequirementKinds.ContainsNode, LabelKey = "x", NodeType = NodeType.Cache }, blueprint).Met);
        Assert.False(Run(new ChallengeRequirement { Kind = RequirementKinds.ContainsNode, LabelKey = "x", NodeType = NodeType.Queue }, blueprint).Met);

        var two = Run(new ChallengeRequirement { Kind = RequirementKinds.ContainsNode, LabelKey = "x", NodeType = NodeType.Cache, Min = 2 }, blueprint);
        Assert.False(two.Met);
        Assert.Equal("1", two.Detail.Arg("count"));
    }

    [Fact]
    public void AbsentNode_points_at_what_is_still_there()
    {
        var result = Run(new ChallengeRequirement { Kind = RequirementKinds.AbsentNode, LabelKey = "x", NodeType = NodeType.Database }, ApiCacheDb());

        Assert.False(result.Met);
        Assert.Contains("Db", result.Detail.Arg("nodes"));
        Assert.Single(result.Elements);
    }

    [Fact]
    public void MaxFanOut_names_the_worst_offender()
    {
        var blueprint = Blueprint.Of(
            [("Api", NodeType.Api), ("A", NodeType.Service), ("B", NodeType.Service), ("C", NodeType.Service)],
            [("Api", "A", true, "{}"),
             ("Api", "B", true, "{}"),
             ("Api", "C", true, "{}")]);

        Assert.True(Run(new ChallengeRequirement { Kind = RequirementKinds.MaxFanOut, LabelKey = "x", Max = 3 }, blueprint).Met);

        var tight = Run(new ChallengeRequirement { Kind = RequirementKinds.MaxFanOut, LabelKey = "x", Max = 2 }, blueprint);
        Assert.False(tight.Met);
        Assert.Equal("challenge.requirement.maxFanOut.exceeded(count=3, max=2, node=Api)", tight.Detail.Describe());
    }

    // Not waiting is legitimate when a broker holds the work; ticking the box off on a straight call
    // is not a way of having a timeout (QA 30-08, §3.3).
    [Fact]
    public void AllEdgesTo_lets_a_call_through_a_broker_pass_and_not_one_that_simply_does_not_wait()
    {
        var published = Blueprint.Of(
            [("Api", NodeType.Api), ("Queue", NodeType.Queue), ("Db", NodeType.Database)],
            [("Api", "Queue", false, "{}"),
             ("Api", "Db", true, """{"timeoutSeconds":5}""")]);

        Assert.True(Run(new ChallengeRequirement
        {
            Kind = RequirementKinds.AllEdgesTo, LabelKey = "x", NodeType = NodeType.Queue, Mechanism = RequirementMechanisms.Timeout
        }, published).Met);

        var delivered = Blueprint.Of(
            [("Api", NodeType.Api), ("Queue", NodeType.Queue), ("Worker", NodeType.Service)],
            [("Api", "Queue", false, "{}"),
             ("Queue", "Worker", false, "{}"),
             ("Api", "Worker", true, """{"timeoutSeconds":5}""")]);

        var timeouts = new ChallengeRequirement
        {
            Kind = RequirementKinds.AllEdgesTo, LabelKey = "x", NodeType = NodeType.Service, Mechanism = RequirementMechanisms.Timeout
        };

        Assert.True(Run(timeouts, delivered).Met);

        var unticked = Blueprint.Of(
            [("Api", NodeType.Api), ("A", NodeType.Service), ("B", NodeType.Service)],
            [("Api", "A", false, "{}"),
             ("Api", "B", true, """{"timeoutSeconds":5}""")]);

        var result = Run(timeouts, unticked);
        Assert.False(result.Met);
        Assert.Equal("challenge.requirement.allEdgesTo.asynchronous", result.Detail.Key());
        Assert.Equal("1", result.Detail.Arg("count"));
        Assert.Equal("2", result.Detail.Arg("total"));
        Assert.Contains("'A'", result.Detail.Arg("calls"));
        Assert.Single(result.Elements);
    }

    [Fact]
    public void AllEdgesTo_reports_how_many_of_how_many()
    {
        var blueprint = Blueprint.Of(
            [("Api", NodeType.Api), ("A", NodeType.Service), ("B", NodeType.Service)],
            [("Api", "A", true, """{"timeoutSeconds":5}"""),
             ("Api", "B", true, "{}")]);

        var result = Run(new ChallengeRequirement
        {
            Kind = RequirementKinds.AllEdgesTo, LabelKey = "x", NodeType = NodeType.Service, Mechanism = RequirementMechanisms.Timeout
        }, blueprint);

        Assert.False(result.Met);
        Assert.Equal("challenge.requirement.allEdgesTo.some", result.Detail.Key());
        Assert.Equal("1", result.Detail.Arg("count"));
        Assert.Equal("2", result.Detail.Arg("total"));
        Assert.Single(result.Elements);
    }

    [Fact]
    public void PathExists_follows_the_direction_of_the_calls()
    {
        var blueprint = ApiCacheDb();

        Assert.True(Run(new ChallengeRequirement { Kind = RequirementKinds.PathExists, LabelKey = "x", FromType = NodeType.Api, ToType = NodeType.Database }, blueprint).Met);

        Assert.False(Run(new ChallengeRequirement { Kind = RequirementKinds.PathExists, LabelKey = "x", FromType = NodeType.Database, ToType = NodeType.Api }, blueprint).Met);
    }

    [Fact]
    public void NoFinding_ignores_the_Good_finding_a_passing_rule_produces()
    {
        var analysis = new AnalysisResult(
            AnalysisResult.CurrentScoringVersion,
            100,
            [],
            new AnalysisSummary(0, 0, 0, 1),
            [new RuleFinding(RuleCodes.Spof, Area.Availability, Severity.Good, LocalizedText.Of("Todo bien."), LocalizedText.Of("Porque sí."), null, [])]);

        var result = RequirementEvaluator.Evaluate(
            new ChallengeRequirement { Kind = RequirementKinds.NoFinding, LabelKey = "x", Code = RuleCodes.Spof },
            ChallengeHarness.Context(ApiCacheDb(), analysis));

        Assert.True(result.Met);
    }

    [Fact]
    public void NoSeverity_covers_that_severity_and_worse()
    {
        var analysis = new AnalysisResult(
            AnalysisResult.CurrentScoringVersion,
            40,
            [],
            new AnalysisSummary(1, 0, 0, 0),
            [new RuleFinding(RuleCodes.Spof, Area.Availability, Severity.Critical, LocalizedText.Of("Hay un SPOF."), LocalizedText.Of("Por esto."), LocalizedText.Of("Haz esto."), [])]);

        var context = ChallengeHarness.Context(ApiCacheDb(), analysis);

        Assert.False(RequirementEvaluator.Evaluate(
            new ChallengeRequirement { Kind = RequirementKinds.NoSeverity, LabelKey = "x", Severity = Severity.Warning }, context).Met);

        Assert.False(RequirementEvaluator.Evaluate(
            new ChallengeRequirement { Kind = RequirementKinds.NoSeverity, LabelKey = "x", Severity = Severity.Critical }, context).Met);
    }

    [Fact]
    public void UnderScenario_fails_when_the_component_it_names_is_gone()
    {
        var result = Run(new ChallengeRequirement
        {
            Kind = RequirementKinds.UnderScenario,
            LabelKey = "x",
            Faults = [new ScenarioFault("Payments", "down")],
            Metric = RequirementMetrics.EntryPointsAffected,
            Operator = RequirementOperators.Equal,
            Value = 0
        }, ApiCacheDb());

        Assert.False(result.Met);
        Assert.Equal("Payments", result.Detail.Arg("node"));
    }

    [Fact]
    public void Requirements_over_the_same_scenario_share_one_simulation()
    {
        var context = ChallengeHarness.Context(ApiCacheDb());

        ChallengeRequirement Over(string metric) => new()
        {
            Kind = RequirementKinds.UnderScenario,
            LabelKey = metric,
            Faults = [new ScenarioFault("Db", "down")],
            Metric = metric,
            Operator = RequirementOperators.GreaterOrEqual,
            Value = 0
        };

        RequirementEvaluator.Evaluate(Over(RequirementMetrics.ComponentsDown), context);
        RequirementEvaluator.Evaluate(Over(RequirementMetrics.BlastRadius), context);
        RequirementEvaluator.Evaluate(Over(RequirementMetrics.EntryPointsAffected), context);

        Assert.Equal(1, context.SimulationsRun);
    }

    [Fact]
    public void No_requirement_reads_the_score()
    {
        var findings = new List<RuleFinding>
        {
            new(RuleCodes.MissingCache, Area.Scalability, Severity.Warning, LocalizedText.Of("Falta una caché."), LocalizedText.Of("Por esto."), LocalizedText.Of("Haz esto."), [])
        };

        AnalysisResult With(int? score, AreaState state) => new(
            AnalysisResult.CurrentScoringVersion,
            score,
            [new AreaScore(Area.Availability, score, state), new AreaScore(Area.Security, score, state)],
            new AnalysisSummary(0, 1, 0, 3),
            findings);

        var blueprint = ApiCacheDb();
        var requirements = new[]
        {
            new ChallengeRequirement { Kind = RequirementKinds.ContainsNode, LabelKey = "a", NodeType = NodeType.Cache },
            new ChallengeRequirement { Kind = RequirementKinds.AbsentNode, LabelKey = "b", NodeType = NodeType.Queue },
            new ChallengeRequirement { Kind = RequirementKinds.MaxFanOut, LabelKey = "c", Max = 1 },
            new ChallengeRequirement { Kind = RequirementKinds.AllEdgesTo, LabelKey = "d", NodeType = NodeType.Database, Mechanism = RequirementMechanisms.Timeout },
            new ChallengeRequirement { Kind = RequirementKinds.PathExists, LabelKey = "e", FromType = NodeType.Api, ToType = NodeType.Database },
            new ChallengeRequirement { Kind = RequirementKinds.NoFinding, LabelKey = "f", Code = RuleCodes.MissingCache },
            new ChallengeRequirement { Kind = RequirementKinds.HasFinding, LabelKey = "g", Code = RuleCodes.MissingCache },
            new ChallengeRequirement { Kind = RequirementKinds.NoSeverity, LabelKey = "h", Severity = Severity.Warning },
            new ChallengeRequirement
            {
                Kind = RequirementKinds.UnderScenario, LabelKey = "i",
                Faults = [new ScenarioFault("Db", "down")],
                Metric = RequirementMetrics.ComponentsDown, Operator = RequirementOperators.LessOrEqual, Value = 1
            }
        };

        var perfect = ChallengeHarness.Context(blueprint, With(100, AreaState.Scored));
        var awful = ChallengeHarness.Context(blueprint, With(null, AreaState.NoRules));

        Assert.Equal(
            requirements.Select(r => (r.Kind, RequirementEvaluator.Evaluate(r, perfect).Met, RequirementEvaluator.Evaluate(r, perfect).Detail.Describe())),
            requirements.Select(r => (r.Kind, RequirementEvaluator.Evaluate(r, awful).Met, RequirementEvaluator.Evaluate(r, awful).Detail.Describe())));
    }

    [Fact]
    public void Edge_asked_not_to_wait_accepts_the_call_that_goes_through_a_queue()
    {
        var requirement = new ChallengeRequirement { Kind = RequirementKinds.Edge, LabelKey = "x", From = "Api", To = "Mailer", Synchronous = false };

        var through = Blueprint.Of(
            [("Api", NodeType.Api), ("Emails", NodeType.Queue), ("Mailer", NodeType.Service)],
            [("Api", "Emails", false, "{}"), ("Emails", "Mailer", false, "{}")]);

        Assert.True(Run(requirement, through).Met);

        var rpc = Blueprint.Of(
            [("Api", NodeType.Api), ("Emails", NodeType.Queue), ("Mailer", NodeType.Service)],
            [("Api", "Emails", false, "{}"), ("Emails", "Mailer", true, "{}")]);

        var waiting = Run(requirement, rpc);
        Assert.False(waiting.Met);
        Assert.Equal("challenge.requirement.edge.notAsynchronous", waiting.Detail.Key());

        var elsewhere = Blueprint.Of(
            [("Api", NodeType.Api), ("Emails", NodeType.Queue), ("Mailer", NodeType.Service), ("Other", NodeType.Worker)],
            [("Api", "Emails", false, "{}"), ("Emails", "Other", false, "{}")]);

        Assert.Equal("challenge.requirement.edge.missing", Run(requirement, elsewhere).Detail.Key());
    }
}

public class NewRequirementKindsTests
{
    private static ChallengeRequirementResult Run(ChallengeRequirement requirement, ArchitectureBlueprint blueprint) =>
        RequirementEvaluator.Evaluate(requirement, ChallengeHarness.Context(blueprint));

    private static ArchitectureBlueprint ApiDb(string apiConfiguration = "{}", string dbConfiguration = "{}") => Blueprint.Of(
        [("Api", NodeType.Api, apiConfiguration), ("Db", NodeType.Database, dbConfiguration)],
        [("Api", "Db", true, """{"timeoutSeconds":5}""")]);

    [Fact]
    public void SpansZones_counts_declared_zones_and_an_unplaced_copy_is_not_somewhere_else()
    {
        static ArchitectureBlueprint Apis(string first, string second) => Blueprint.Of(
            [("Front", NodeType.LoadBalancer, "{}"), ("Api", NodeType.Api, first), ("Api 2", NodeType.Api, second)],
            [("Front", "Api", true, "{}"), ("Front", "Api 2", true, "{}")]);

        var requirement = new ChallengeRequirement { Kind = RequirementKinds.SpansZones, LabelKey = "x", NodeType = NodeType.Api };

        Assert.True(Run(requirement, Apis("""{"zone":"a"}""", """{"zone":"b"}""")).Met);

        var same = Run(requirement, Apis("""{"zone":"a"}""", """{"zone":"a"}"""));
        Assert.False(same.Met);
        Assert.Equal("challenge.requirement.spansZones.tooFew", same.Detail.Key());
        Assert.Equal("1", same.Detail.Arg("count"));
        Assert.Equal(2, same.Elements.Count);

        var hidden = Run(requirement, Apis("""{"zone":"a"}""", "{}"));
        Assert.False(hidden.Met);
        Assert.Equal("challenge.requirement.spansZones.unplaced", hidden.Detail.Key());
        Assert.Equal("'Api 2'", hidden.Detail.Arg("nodes"));
        Assert.Single(hidden.Elements);
    }

    [Fact]
    public void DeclaresLoad_counts_the_entry_points_and_not_a_loose_box()
    {
        var blueprint = Blueprint.Of(
            [("Api", NodeType.Api, """{"requestsPerSecond":100}"""), ("Db", NodeType.Database, "{}"), ("Loose", NodeType.Api, """{"requestsPerSecond":900}""")],
            [("Api", "Db", true, """{"timeoutSeconds":5}""")]);

        var result = Run(new ChallengeRequirement { Kind = RequirementKinds.DeclaresLoad, LabelKey = "x", Min = 300 }, blueprint);

        Assert.False(result.Met);
        Assert.Equal("100", result.Detail.Arg("declared"));
    }

    [Fact]
    public void MaxNodes_counts_the_whole_inventory()
    {
        Assert.True(Run(new ChallengeRequirement { Kind = RequirementKinds.MaxNodes, LabelKey = "x", Max = 2 }, ApiDb()).Met);

        var tight = Run(new ChallengeRequirement { Kind = RequirementKinds.MaxNodes, LabelKey = "x", Max = 1 }, ApiDb());

        Assert.False(tight.Met);
        Assert.Equal("challenge.requirement.maxNodes.exceeded(count=2, max=1)", tight.Detail.Describe());
    }

    [Fact]
    public void MaxInstances_reads_the_same_copies_the_capacity_model_reads()
    {
        var scaled = ApiDb(dbConfiguration: """{"readReplicas":3}""");

        var capped = Run(new ChallengeRequirement { Kind = RequirementKinds.MaxInstances, LabelKey = "x", NodeType = NodeType.Database, Max = 1 }, scaled);

        Assert.False(capped.Met);
        Assert.Equal("challenge.requirement.maxInstances.exceeded(count=4, max=1, node=Db)", capped.Detail.Describe());
        Assert.Single(capped.Elements);
    }

    [Fact]
    public void MaxInstances_without_a_type_caps_every_component()
    {
        var blueprint = ApiDb(apiConfiguration: """{"instances":4}""");

        Assert.False(Run(new ChallengeRequirement { Kind = RequirementKinds.MaxInstances, LabelKey = "x", Max = 1 }, blueprint).Met);
        Assert.True(Run(new ChallengeRequirement { Kind = RequirementKinds.MaxInstances, LabelKey = "x", NodeType = NodeType.Database, Max = 1 }, blueprint).Met);
    }

    [Fact]
    public void MaxCost_separates_the_two_ways_out_of_database_overload()
    {
        // The whole point of C5, in one test: both shapes have three nodes and two edges, so no
        // maxNodes can tell them apart. The budget can.
        var iron = Blueprint.Of(
            [("Api", NodeType.Api, "{}"), ("Db", NodeType.Database, """{"readReplicas":1}""")],
            [("Api", "Db", true, """{"timeoutSeconds":5}""")]);

        var cache = Blueprint.Of(
            [("Api", NodeType.Api), ("Cache", NodeType.Cache), ("Db", NodeType.Database)],
            [("Api", "Cache", true, """{"timeoutSeconds":2}"""),
             ("Cache", "Db", true, """{"timeoutSeconds":5}""")]);

        var budget = new ChallengeRequirement { Kind = RequirementKinds.MaxCost, LabelKey = "x", Max = 18 };

        Assert.Equal(24, CostModel.Of(ChallengeHarness.Graph(iron)));
        Assert.Equal(16, CostModel.Of(ChallengeHarness.Graph(cache)));

        Assert.False(Run(budget, iron).Met);
        Assert.True(Run(budget, cache).Met);
    }

    [Fact]
    public void MaxCost_says_where_the_money_goes_and_not_only_that_you_went_over()
    {
        var blueprint = Blueprint.Of(
            [("Api", NodeType.Api, "{}"), ("Db", NodeType.Database, """{"readReplicas":1}""")],
            [("Api", "Db", true, """{"timeoutSeconds":5}""")]);

        var result = Run(new ChallengeRequirement { Kind = RequirementKinds.MaxCost, LabelKey = "x", Max = 18 }, blueprint);

        Assert.False(result.Met);
        Assert.Equal(
            "challenge.requirement.maxCost.exceeded(cost=20, max=18, node=Db, total=24)",
            result.Detail.Describe());
    }

    [Fact]
    public void MaxCost_reads_the_same_total_the_cost_model_publishes()
    {
        var blueprint = Blueprint.Of(
            [("Api", NodeType.Api, """{"instances":3}"""), ("Db", NodeType.Database, "{}"), ("Nobody", NodeType.ExternalService, "{}")],
            [("Api", "Db", true, """{"timeoutSeconds":5}""")]);

        var total = CostModel.Of(ChallengeHarness.Graph(blueprint));

        Assert.Equal(22, total);
        Assert.True(Run(new ChallengeRequirement { Kind = RequirementKinds.MaxCost, LabelKey = "x", Max = total }, blueprint).Met);
        Assert.False(Run(new ChallengeRequirement { Kind = RequirementKinds.MaxCost, LabelKey = "x", Max = total - 1 }, blueprint).Met);
    }

    [Fact]
    public void MaxCost_never_points_at_what_nobody_bills_you_for()
    {
        var blueprint = Blueprint.Of(
            [("Browser", NodeType.Client), ("Api", NodeType.Api), ("Stripe", NodeType.ExternalService)],
            [("Browser", "Api", true, "{}"),
             ("Api", "Stripe", true, """{"timeoutSeconds":5}""")]);

        var result = Run(new ChallengeRequirement { Kind = RequirementKinds.MaxCost, LabelKey = "x", Max = 1 }, blueprint);

        Assert.False(result.Met);
        Assert.Equal("Api", result.Detail.Arg("node"));
        Assert.Single(result.Elements);
    }

    [Fact]
    public void Edge_asks_about_one_call_and_says_so_when_it_is_gone()
    {
        var missing = Run(
            new ChallengeRequirement { Kind = RequirementKinds.Edge, LabelKey = "x", From = "Api", To = "Mailer", Synchronous = false },
            ApiDb());

        Assert.False(missing.Met);
        Assert.Equal("challenge.requirement.edge.missing(from=Api, to=Mailer)", missing.Detail.Describe());
    }

    [Fact]
    public void Edge_tells_a_synchronous_call_from_an_asynchronous_one()
    {
        var requirement = new ChallengeRequirement { Kind = RequirementKinds.Edge, LabelKey = "x", From = "Api", To = "Db", Synchronous = false };

        var blocking = Run(requirement, ApiDb());
        Assert.False(blocking.Met);
        Assert.Equal("challenge.requirement.edge.notAsynchronous(from=Api, to=Db)", blocking.Detail.Describe());
        Assert.Single(blocking.Elements);

        var released = Blueprint.Of(
            [("Api", NodeType.Api), ("Db", NodeType.Database)],
            [("Api", "Db", false, "{}")]);

        Assert.True(Run(requirement, released).Met);
    }

    [Fact]
    public void Edge_with_a_mechanism_fails_the_call_that_does_not_wait()
    {
        var unticked = Blueprint.Of(
            [("Api", NodeType.Api), ("Db", NodeType.Database)],
            [("Api", "Db", false, """{"timeoutSeconds":5}""")]);

        var result = Run(new ChallengeRequirement
        {
            Kind = RequirementKinds.Edge, LabelKey = "x", From = "Api", To = "Db", Mechanism = RequirementMechanisms.Timeout
        }, unticked);

        Assert.False(result.Met);
        Assert.Equal("challenge.requirement.edge.asynchronous", result.Detail.Key());
        Assert.Equal("mechanism.timeout", result.Detail.Arg("mechanism"));
        Assert.Single(result.Elements);
    }

    [Fact]
    public void An_edge_asked_not_to_wait_cannot_be_asked_a_mechanism()
    {
        var contradiction = new ChallengeRequirement
        {
            Kind = RequirementKinds.Edge, LabelKey = "challenge.x.y", From = "Api", To = "Mailer",
            Synchronous = false, Mechanism = RequirementMechanisms.Timeout
        };

        var message = Assert.Throws<InvalidChallengeDefinitionException>(
            () => ChallengeCatalog.ValidateRequirement("x", contradiction)).Message;

        Assert.Contains("nothing to declare", message);
    }

    [Fact]
    public void Edge_asks_a_mechanism_only_of_the_call_it_names()
    {
        var blueprint = Blueprint.Of(
            [("Api", NodeType.Api), ("Db", NodeType.Database), ("Cache", NodeType.Cache)],
            [("Api", "Db", true, """{"timeoutSeconds":5}"""),
             ("Api", "Cache", true, "{}")]);

        Assert.True(Run(new ChallengeRequirement
        {
            Kind = RequirementKinds.Edge, LabelKey = "x", From = "Api", To = "Db", Mechanism = RequirementMechanisms.Timeout
        }, blueprint).Met);

        var fallback = Run(new ChallengeRequirement
        {
            Kind = RequirementKinds.Edge, LabelKey = "x", From = "Api", To = "Db", Mechanism = RequirementMechanisms.Fallback
        }, blueprint);

        Assert.False(fallback.Met);
        Assert.Equal("mechanism.fallback", fallback.Detail.Arg("mechanism"));
    }
}

internal static class Blueprint
{
    public static ArchitectureBlueprint Of(
        (string Name, NodeType Type)[] nodes,
        (string From, string To, bool Synchronous, string Configuration)[] edges) =>
        Of(nodes.Select(n => (n.Name, n.Type, "{}")).ToArray(), edges);

    public static ArchitectureBlueprint Of(
        (string Name, NodeType Type, string Configuration)[] nodes,
        (string From, string To, bool Synchronous, string Configuration)[] edges) => new()
    {
        Name = "test",
        Nodes = nodes.Select(n => new BlueprintNode
        {
            Name = n.Name,
            Type = n.Type,
            Configuration = System.Text.Json.JsonDocument.Parse(n.Configuration).RootElement.Clone()
        }).ToList(),
        Connections = edges.Select(e => new BlueprintConnection
        {
            From = e.From,
            To = e.To,
            Synchronous = e.Synchronous,
            Configuration = System.Text.Json.JsonDocument.Parse(e.Configuration).RootElement.Clone()
        }).ToList()
    };
}
