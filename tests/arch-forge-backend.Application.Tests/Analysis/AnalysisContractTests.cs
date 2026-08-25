using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis;

/// <summary>
/// V16 C5 and C7a. The two fields <c>/analyze</c> gained, and they are both the same kind of
/// change: <strong>numbers the analyzer already had and threw away</strong>. Nothing here is about
/// what the analyzer finds - the four reference architectures are still 98 / 92 / 83 / 70, pinned
/// where they always were in <see cref="ReferenceArchitectureTests"/> - and everything here is
/// about what leaves the analyzer.
///
/// The risk each half runs is different, which is why they are tested differently. Coverage can
/// only be wrong by <em>disagreeing with the findings beside it in the same payload</em>, so it is
/// checked against them over all four references at once. The load meter can only be wrong by
/// reporting a number the rule that grades it does not recognize, so it is checked on a graph
/// whose arithmetic is written out by hand.
/// </summary>
public class AnalysisContractTests
{
    private static readonly string[] References =
        ["minimal-api-db", "resilient-async", "gateway-microservices", "worst-case"];

    // -------------------------------------------------------------------------------------------
    // C7a - the coverage of the twenty-four
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void Coverage_names_every_registered_rule_exactly_once()
    {
        // The whole point of the field: a rule exists whether or not it had anything to say here.
        // Twenty-four lines on every architecture, the empty one included.
        foreach (var name in References)
        {
            var coverage = ReferenceArchitecture.Analyze(name).Coverage;

            Assert.Equal(24, coverage.Count);
            Assert.Equal(24, coverage.Select(rule => rule.Code).Distinct().Count());
        }
    }

    [Fact]
    public void Coverage_is_ordered_by_area_and_then_by_code()
    {
        // D2.1, rule 4, applied to the catalogue: the screen reading this groups by area, and the
        // order must be a property of the payload rather than of the order Program.cs registers
        // the rules in.
        var coverage = ReferenceArchitecture.Analyze("worst-case").Coverage;

        Assert.Equal(
            coverage.OrderBy(rule => rule.Area).ThenBy(rule => rule.Code, StringComparer.Ordinal).ToList(),
            coverage);
    }

    [Fact]
    public void Coverage_says_the_same_thing_as_the_findings_beside_it()
    {
        // Two readings of one analysis travel in the same payload, and a screen that showed a rule
        // as "passed" while its Critical sits in the panel would be worse than not showing it. The
        // three states are exactly the three shapes the findings of a code can have.
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
                        // Nothing at all: not applicable is not a pass, and D1.4 says it costs the
                        // area nothing either.
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
        // AnalysisSummary.Passed has counted "applicable rules that found nothing" since V2 D1.4.
        // The catalogue paints one tick per rule; the summary prints the total. Two numbers that
        // disagree on the same screen is the failure this pins.
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
        // The state C7 calls the most valuable of the three, on the smallest correct drawing there
        // is. Nobody declared any traffic, so UNDERSIZED_COMPONENT never ran - which is a different
        // statement from "your components are the right size", and until now the product could not
        // make it.
        var result = ReferenceArchitecture.Analyze("minimal-api-db");

        Assert.Equal(RuleState.NotApplicable, StateOf(result, RuleCodes.UndersizedComponent));
        Assert.Equal(RuleState.NotApplicable, StateOf(result, RuleCodes.QueueNoDlq));      // no queue drawn
        Assert.Equal(RuleState.Passed, StateOf(result, RuleCodes.Spof));                   // it ran, and found none
        Assert.Equal(RuleState.Fired, StateOf(result, RuleCodes.NoObservability));         // nothing is watching
    }

    [Fact]
    public void Coverage_moves_with_the_drawing()
    {
        // The same rule, the two states that are easy to confuse. Draw the queue and
        // QUEUE_NO_DLQ stops being "not applicable" and starts being an answer.
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

    // -------------------------------------------------------------------------------------------
    // C7b - the reason a rule never got to look
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void Every_rule_that_stood_down_says_why_and_says_it_in_its_own_name()
    {
        // The two halves of C7b that can rot separately. A missing sentence is a blank line on the
        // catalogue's most valuable state, and a sentence filed under another rule's name is worse
        // than blank: the card would answer a question the user did not ask of it. The key names
        // the code it belongs to, so copying a line from a neighbouring rule shows up here.
        foreach (var name in References)
        {
            foreach (var rule in ReferenceArchitecture.Analyze(name).Coverage)
            {
                if (rule.State != RuleState.NotApplicable)
                {
                    // Nothing to explain: the rule ran, and its findings do the talking.
                    Assert.Null(rule.Reason);
                    continue;
                }

                Assert.NotNull(rule.Reason);
                Assert.StartsWith($"rule.na.{rule.Code}.", rule.Reason!.Key, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_empty_canvas_gets_twenty_four_different_sentences()
    {
        // Nothing drawn stands every rule down at once, which makes this the one graph that reaches
        // all twenty-four sentences - and the screen where they matter most, because a user looking
        // at twenty-four grey cards is a user who has drawn nothing yet.
        //
        // Twenty-four *distinct* keys, deliberately: one sentence reused across rules would be a
        // sentence too vague to act on, which is the failure C7b exists to avoid.
        var coverage = ReferenceArchitecture.Analyze(new GraphBuilder().Build()).Coverage;

        Assert.All(coverage, rule => Assert.Equal(RuleState.NotApplicable, rule.State));
        Assert.Equal(24, coverage.Select(rule => rule.Reason!.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_rule_that_stands_down_twice_for_different_reasons_says_two_different_things()
    {
        // One key per *situation*, not per rule. MISSING_CDN is the clearest of the three that do
        // this: "you have not declared any load" and "no door of yours is busy enough" are two
        // different things to go and draw, and a single sentence covering both would name neither.
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

        // And the number in that sentence is the threshold the rule compares against, sent as an
        // argument rather than written into the prose: two copies of 1 000 is one of them going
        // stale the day the threshold moves.
        Assert.Equal(
            RuleThresholds.CdnWorthTraffic,
            ReasonOf(quiet, RuleCodes.MissingCdn).Args!["traffic"]);
    }

    [Fact]
    public void The_reason_goes_away_the_moment_the_drawing_answers_it()
    {
        // The pair QUEUE_NO_DLQ makes on the two graphs of `Coverage_moves_with_the_drawing`: the
        // sentence is about *this* drawing, so it has to disappear when the drawing changes, not
        // linger next to a finding.
        var withQueue = ReferenceArchitecture.Analyze(new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Cola", NodeType.Queue).Node("W", NodeType.Worker)
            .Connect("Api", "Cola", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Cola", "W", synchronous: false, protocol: ProtocolType.Amqp)
            .Build());

        Assert.Equal(RuleState.Fired, StateOf(withQueue, RuleCodes.QueueNoDlq));
        Assert.Null(withQueue.Coverage.Single(rule => rule.Code == RuleCodes.QueueNoDlq).Reason);
    }

    // -------------------------------------------------------------------------------------------
    // C5 - the load meter
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void Load_says_nothing_when_nobody_declared_traffic()
    {
        // "Not measured" is not "idle" - the distinction ElementState.Utilization makes with a
        // null, made here by an empty list. Every drawing made before V4 is in this case, and so
        // are all four references.
        foreach (var name in References)
            Assert.Empty(ReferenceArchitecture.Analyze(name).Load);
    }

    [Fact]
    public void Load_publishes_the_utilisation_the_rule_grades_on()
    {
        // 340 rps into an Api that can take 500, and on into a database that can take 200. The
        // arithmetic is CapacityModel's and it is done here by hand on purpose: 340/500 = 0,68 and
        // 340/200 = 1,7.
        //
        // **And this is the whole of C5 in one assertion**: the Api reads 68 % and
        // UNDERSIZED_COMPONENT has nothing to say about it, because the rule only speaks from 80 %.
        // The meter is what lets the user see the bar filling three edits before the finding
        // appears.
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

        // The rule and the meter read the same number: over capacity is a Critical, and it is the
        // database's.
        var finding = result.Findings.Single(f => f.Code == RuleCodes.UndersizedComponent);
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal(graph.NodeId("Db"), finding.Elements[0].Id);
    }

    [Fact]
    public void Load_covers_every_component_including_the_ones_nobody_can_size()
    {
        // One entry per box, in the graph's canonical order. The Client is here with the enormous
        // capacity CapacityModel hands it *so that it never saturates*: what to paint over a box
        // like that is the canvas's decision, and it is not made by leaving it out of the
        // measurement.
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
        Assert.Equal(0m, users.Utilization);    // 340 over a million, which is what "not yours to size" looks like
    }

    [Fact]
    public void Load_reads_the_instances_declared_on_the_component()
    {
        // The lever of the product: capacity is per instance times the instances declared, and the
        // meter has to move when the user pulls it - otherwise the bar contradicts the fix the
        // finding just offered.
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
        // DT.3 and V14 §4.3: a rate limit rejects at the door. The meter is a meter of the
        // component, so it shows the 100 rps the Api is actually serving - the 4 000 that were
        // turned away are TRAFFIC_REJECTED's sentence and not a bar on a box.
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
        // The two axes read one LoadModel and round it the same way, which is the property that
        // stops the canvas from painting 68 % while you draw and 67,5 % the moment you simulate.
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

    // -------------------------------------------------------------------------------------------

    private static LocalizedText ReasonOf(AnalysisResult result, string code) =>
        result.Coverage.Single(rule => rule.Code == code).Reason!;

    private static RuleState StateOf(AnalysisResult result, string code) =>
        result.Coverage.Single(rule => rule.Code == code).State;

    private static ComponentLoad LoadOf(AnalysisResult result, ArchitectureGraph graph, string name) =>
        result.Load.Single(entry => entry.NodeId == graph.NodeId(name));
}
