using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Challenges;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Challenges;

/// <summary>
/// The catalogue of D3.1, kind by kind, over blueprints small enough to read. These are the tests
/// that let a challenge author trust a requirement means what its label says.
/// </summary>
public class RequirementEvaluatorTests
{
    private static ArchitectureBlueprint ApiCacheDb() => Blueprint.Of(
        [("Api", NodeType.Api), ("Cache", NodeType.Cache), ("Db", NodeType.Database)],
        [("Api", "Cache", ProtocolType.Http, true, """{"timeoutSeconds":2}"""),
         ("Cache", "Db", ProtocolType.Sql, true, """{"timeoutSeconds":5}""")]);

    private static ChallengeRequirementResult Run(ChallengeRequirement requirement, ArchitectureBlueprint blueprint) =>
        RequirementEvaluator.Evaluate(requirement, ChallengeHarness.Context(blueprint));

    [Fact]
    public void ContainsNode_counts_by_type()
    {
        var blueprint = ApiCacheDb();

        Assert.True(Run(new ChallengeRequirement { Kind = RequirementKinds.ContainsNode, Label = "x", NodeType = NodeType.Cache }, blueprint).Met);
        Assert.False(Run(new ChallengeRequirement { Kind = RequirementKinds.ContainsNode, Label = "x", NodeType = NodeType.Queue }, blueprint).Met);

        var two = Run(new ChallengeRequirement { Kind = RequirementKinds.ContainsNode, Label = "x", NodeType = NodeType.Cache, Min = 2 }, blueprint);
        Assert.False(two.Met);
        Assert.Contains("1", two.Detail);
    }

    [Fact]
    public void AbsentNode_points_at_what_is_still_there()
    {
        var result = Run(new ChallengeRequirement { Kind = RequirementKinds.AbsentNode, Label = "x", NodeType = NodeType.Database }, ApiCacheDb());

        Assert.False(result.Met);
        Assert.Contains("'Db'", result.Detail);
        Assert.Single(result.Elements);
    }

    [Fact]
    public void MaxFanOut_names_the_worst_offender()
    {
        var blueprint = Blueprint.Of(
            [("Api", NodeType.Api), ("A", NodeType.Service), ("B", NodeType.Service), ("C", NodeType.Service)],
            [("Api", "A", ProtocolType.Http, true, "{}"),
             ("Api", "B", ProtocolType.Http, true, "{}"),
             ("Api", "C", ProtocolType.Http, true, "{}")]);

        Assert.True(Run(new ChallengeRequirement { Kind = RequirementKinds.MaxFanOut, Label = "x", Max = 3 }, blueprint).Met);

        var tight = Run(new ChallengeRequirement { Kind = RequirementKinds.MaxFanOut, Label = "x", Max = 2 }, blueprint);
        Assert.False(tight.Met);
        Assert.Contains("'Api' llama directamente a 3", tight.Detail);
    }

    [Fact]
    public void AllEdgesTo_ignores_asynchronous_calls()
    {
        // V3 D4.2: on an asynchronous edge nobody waits, so there is no error for a timeout to
        // catch. Demanding one there would be asking for a checkbox.
        var blueprint = Blueprint.Of(
            [("Api", NodeType.Api), ("Queue", NodeType.Queue), ("Db", NodeType.Database)],
            [("Api", "Queue", ProtocolType.Amqp, false, "{}"),
             ("Api", "Db", ProtocolType.Sql, true, """{"timeoutSeconds":5}""")]);

        Assert.True(Run(new ChallengeRequirement
        {
            Kind = RequirementKinds.AllEdgesTo, Label = "x", NodeType = NodeType.Queue, Mechanism = RequirementMechanisms.Timeout
        }, blueprint).Met);
    }

    [Fact]
    public void AllEdgesTo_reports_how_many_of_how_many()
    {
        var blueprint = Blueprint.Of(
            [("Api", NodeType.Api), ("A", NodeType.Service), ("B", NodeType.Service)],
            [("Api", "A", ProtocolType.Http, true, """{"timeoutSeconds":5}"""),
             ("Api", "B", ProtocolType.Http, true, "{}")]);

        var result = Run(new ChallengeRequirement
        {
            Kind = RequirementKinds.AllEdgesTo, Label = "x", NodeType = NodeType.Service, Mechanism = RequirementMechanisms.Timeout
        }, blueprint);

        Assert.False(result.Met);
        Assert.Contains("1 de las 2", result.Detail);
        Assert.Single(result.Elements);
    }

    [Fact]
    public void PathExists_follows_the_direction_of_the_calls()
    {
        var blueprint = ApiCacheDb();

        Assert.True(Run(new ChallengeRequirement { Kind = RequirementKinds.PathExists, Label = "x", FromType = NodeType.Api, ToType = NodeType.Database }, blueprint).Met);

        // The other way round there is no path: source depends on target, never the reverse (D3.3 of V2).
        Assert.False(Run(new ChallengeRequirement { Kind = RequirementKinds.PathExists, Label = "x", FromType = NodeType.Database, ToType = NodeType.Api }, blueprint).Met);
    }

    /// <summary>
    /// The analyzer synthesizes a <c>Good</c> finding carrying the rule's own code when the rule
    /// passes (V2 D1.4). Counting those would make <c>noFinding</c> unsatisfiable: it would fail
    /// exactly when the rule is happy.
    /// </summary>
    [Fact]
    public void NoFinding_ignores_the_Good_finding_a_passing_rule_produces()
    {
        var analysis = new AnalysisResult(
            AnalysisResult.CurrentScoringVersion,
            100,
            [],
            new AnalysisSummary(0, 0, 1),
            [new RuleFinding(RuleCodes.Spof, Area.Availability, Severity.Good, "Todo bien.", "Porque sí.", null, [])]);

        var result = RequirementEvaluator.Evaluate(
            new ChallengeRequirement { Kind = RequirementKinds.NoFinding, Label = "x", Code = RuleCodes.Spof },
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
            new AnalysisSummary(1, 0, 0),
            [new RuleFinding(RuleCodes.Spof, Area.Availability, Severity.Critical, "Hay un SPOF.", "Por esto.", "Haz esto.", [])]);

        var context = ChallengeHarness.Context(ApiCacheDb(), analysis);

        // Asking for "no warnings" while a critical slipped past would be a checklist that lies.
        Assert.False(RequirementEvaluator.Evaluate(
            new ChallengeRequirement { Kind = RequirementKinds.NoSeverity, Label = "x", Severity = Severity.Warning }, context).Met);

        Assert.False(RequirementEvaluator.Evaluate(
            new ChallengeRequirement { Kind = RequirementKinds.NoSeverity, Label = "x", Severity = Severity.Critical }, context).Met);
    }

    [Fact]
    public void UnderScenario_fails_when_the_component_it_names_is_gone()
    {
        var result = Run(new ChallengeRequirement
        {
            Kind = RequirementKinds.UnderScenario,
            Label = "x",
            Faults = [new ScenarioFault("Payments", "down")],
            Metric = RequirementMetrics.EntryPointsAffected,
            Operator = RequirementOperators.Equal,
            Value = 0
        }, ApiCacheDb());

        Assert.False(result.Met);
        Assert.Contains("Payments", result.Detail);
    }

    [Fact]
    public void Requirements_over_the_same_scenario_share_one_simulation()
    {
        // D3.2. Three requirements over "with Db down" cost one run, not three - which is what makes
        // the cap of three scenarios per challenge a cap on distinct scenarios.
        var context = ChallengeHarness.Context(ApiCacheDb());

        ChallengeRequirement Over(string metric) => new()
        {
            Kind = RequirementKinds.UnderScenario,
            Label = metric,
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

    /// <summary>
    /// <strong>D1.4, and this is the test that keeps it true.</strong> V4 adds seven rules and two
    /// areas that scored nothing start scoring, so every existing score moves. If any predicate read
    /// the score, challenges would un-pass themselves on deploy - so the same architecture and the
    /// same findings, with a wildly different score, must produce an identical verdict.
    /// </summary>
    [Fact]
    public void No_requirement_reads_the_score()
    {
        var findings = new List<RuleFinding>
        {
            new(RuleCodes.MissingCache, Area.Scalability, Severity.Warning, "Falta una caché.", "Por esto.", "Haz esto.", [])
        };

        AnalysisResult With(int? score, AreaState state) => new(
            AnalysisResult.CurrentScoringVersion,
            score,
            [new AreaScore(Area.Availability, score, state), new AreaScore(Area.Security, score, state)],
            new AnalysisSummary(0, 1, 3),
            findings);

        var blueprint = ApiCacheDb();
        var requirements = new[]
        {
            new ChallengeRequirement { Kind = RequirementKinds.ContainsNode, Label = "a", NodeType = NodeType.Cache },
            new ChallengeRequirement { Kind = RequirementKinds.AbsentNode, Label = "b", NodeType = NodeType.Queue },
            new ChallengeRequirement { Kind = RequirementKinds.MaxFanOut, Label = "c", Max = 1 },
            new ChallengeRequirement { Kind = RequirementKinds.AllEdgesTo, Label = "d", NodeType = NodeType.Database, Mechanism = RequirementMechanisms.Timeout },
            new ChallengeRequirement { Kind = RequirementKinds.PathExists, Label = "e", FromType = NodeType.Api, ToType = NodeType.Database },
            new ChallengeRequirement { Kind = RequirementKinds.NoFinding, Label = "f", Code = RuleCodes.MissingCache },
            new ChallengeRequirement { Kind = RequirementKinds.HasFinding, Label = "g", Code = RuleCodes.MissingCache },
            new ChallengeRequirement { Kind = RequirementKinds.NoSeverity, Label = "h", Severity = Severity.Warning },
            new ChallengeRequirement
            {
                Kind = RequirementKinds.UnderScenario, Label = "i",
                Faults = [new ScenarioFault("Db", "down")],
                Metric = RequirementMetrics.ComponentsDown, Operator = RequirementOperators.LessOrEqual, Value = 1
            }
        };

        var perfect = ChallengeHarness.Context(blueprint, With(100, AreaState.Scored));
        var awful = ChallengeHarness.Context(blueprint, With(null, AreaState.NoRules));

        Assert.Equal(
            requirements.Select(r => (r.Kind, RequirementEvaluator.Evaluate(r, perfect).Met, RequirementEvaluator.Evaluate(r, perfect).Detail)),
            requirements.Select(r => (r.Kind, RequirementEvaluator.Evaluate(r, awful).Met, RequirementEvaluator.Evaluate(r, awful).Detail)));
    }
}

/// <summary>Blueprints small enough that the assertion above them reads on its own.</summary>
internal static class Blueprint
{
    public static ArchitectureBlueprint Of(
        (string Name, NodeType Type)[] nodes,
        (string From, string To, ProtocolType Protocol, bool Synchronous, string Configuration)[] edges) => new()
    {
        Name = "test",
        Nodes = nodes.Select(n => new BlueprintNode { Name = n.Name, Type = n.Type }).ToList(),
        Connections = edges.Select(e => new BlueprintConnection
        {
            From = e.From,
            To = e.To,
            Protocol = e.Protocol,
            Synchronous = e.Synchronous,
            Configuration = System.Text.Json.JsonDocument.Parse(e.Configuration).RootElement.Clone()
        }).ToList()
    };
}
