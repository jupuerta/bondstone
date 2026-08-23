using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Fixtures;

/// <summary>
/// The four reference architectures of V2 plan técnico §8, with their scores, areas, codes and
/// order pinned down. The expected numbers were worked out by hand before the rules existed: when
/// one of these fails, the first job is to find out which of the two is wrong, not to adjust the
/// expected value until it passes.
/// </summary>
public class ReferenceArchitectureTests
{
    [Fact]
    public void MinimalApiDb_IsStillPerfectOnTheFourAreasV2Scored()
    {
        // The fixture that proves a correct architecture is not scolded. Without it, nothing stops
        // the analyzer from drifting into telling off the smallest correct thing you can draw.
        //
        // **It no longer scores 100, and that is the point of V4.** The four areas V2 measured are
        // still perfect; the two points it loses are a front door that never said whether it
        // authenticates, and nothing watching. Both are true of these two boxes, and until V4 nobody
        // was asking.
        //
        // **And it no longer carries a single Critical** (V13 D2.1). Until V13, the smallest correct
        // thing you can draw - an Api and a Database - opened with a red one, because an undeclared
        // `authentication` and a declared "no" were the same `false`. Two boxes and an arrow is not
        // a security incident; it is a drawing that has not said yet.
        var result = ReferenceArchitecture.Analyze("minimal-api-db");

        Assert.Equal(100, result.ScoreOf(Area.Availability));
        Assert.Equal(100, result.ScoreOf(Area.Resilience));
        Assert.Equal(100, result.ScoreOf(Area.Coupling));
        Assert.Equal(100, result.ScoreOf(Area.Scalability));

        Assert.Equal(95, result.ScoreOf(Area.Security));        // UNAUTHENTICATED_ENTRY, undeclared
        Assert.Equal(93, result.ScoreOf(Area.Observability));   // NO_OBSERVABILITY on both boxes: 5 + 2,5
        Assert.Equal(98, result.Score);                         // (100·4 + 95 + 93) / 6 = 98,0

        // Eleven since QA §4: SHARED_DATABASE looks at the one database and finds one caller.
        // The other two of that round are not applicable here - no queue, no cache. And three
        // warnings, not two: coverage (QA §4) names the Api and the database separately.
        Assert.Equal(new AnalysisSummary(Critical: 0, Warning: 3, Passed: 11), result.Summary);
    }

    [Fact]
    public void MinimalApiDb_ScoresTheSixAreas()
    {
        // V2 left Security and Observability travelling in the contract with a dash on them, and V3
        // pushed them twice. D5.2 pays that: **the six areas score for the first time**, and no area
        // is a 100 that nobody checked - which is the decorative number D1.2 exists to avoid.
        var result = ReferenceArchitecture.Analyze("minimal-api-db");

        Assert.All(result.Areas, area => Assert.Equal(AreaState.Scored, area.State));
    }

    [Fact]
    public void MinimalApiDb_RetryAmplificationHasNothingToCheck()
    {
        // Nobody retries here, so the rule is not applicable and contributes no passed check
        // (D1.4). Resilience is still scored, because its other two rules did apply.
        var result = ReferenceArchitecture.Analyze("minimal-api-db");

        Assert.DoesNotContain(RuleCodes.RetryAmplification, result.Findings.Select(f => f.Code));
        Assert.Equal(AreaState.Scored, result.StateOf(Area.Resilience));
    }

    [Fact]
    public void GatewayMicroservices_ScoresEightyFive()
    {
        // The most important fixture: a normal microservices architecture, neither exemplary nor a
        // disaster. (75 + 60 + 100 + 95) / 4 = 82.5 -> 83.
        var result = ReferenceArchitecture.Analyze("gateway-microservices");

        Assert.Equal(75, result.ScoreOf(Area.Availability));

        // 60 until V14 B3. `Gateway -> Orders -> Payments -> Db` was four links; it is two services
        // calling each other through a router and into a store, which is under the threshold the
        // rule was calibrated on.
        Assert.Equal(65, result.ScoreOf(Area.Resilience));

        // 100 until QA §4. Three services on one database is the textbook integration-by-database,
        // and this is the fixture it was always drawn in: SHARED_DATABASE names Users, Orders and
        // Payments on Db, and Coupling pays the five points of a Warning.
        Assert.Equal(95, result.ScoreOf(Area.Coupling));
        Assert.Equal(95, result.ScoreOf(Area.Scalability));
        Assert.Equal(95, result.ScoreOf(Area.Security));

        // Five components and nothing watching any of them: 5 + 2,5 × 4 hits the cap of 15 (QA §4,
        // coverage). Until v6 this read 95 - one Warning for a whole system running blind.
        Assert.Equal(85, result.ScoreOf(Area.Observability));

        // **83 in v2, 85 in v4, 87 in v5, 84 in v6, 85 since V14 B3, and the architecture never
        // changed.** V4 started measuring two areas nobody was measuring; V13 D2.1 stopped reading
        // an undeclared field as a decision; v6 started reading a schema three services share and
        // counting what runs blind; B3 stopped counting the gateway and the database as links of a
        // chain, and `Gateway -> Orders -> Payments` is two services, under the threshold the rule
        // was calibrated on. Scores from two scoring versions are not comparable, which is what
        // `scoringVersion` is for (V2 D1.5) and why a challenge may never assert on one (V4 D1.4).
        Assert.Equal(85, result.Score);

        // The gateway moves from the Critical column to the Warning one, so 6 · 5 becomes 5 · 6;
        // SHARED_DATABASE makes the six seven, and coverage turns one NO_OBSERVABILITY into five.
        // V14 B3 takes SYNC_CHAIN's Warning off the count and gives the rule its green tick back.
        Assert.Equal(new AnalysisSummary(Critical: 5, Warning: 10, Passed: 7), result.Summary);
    }

    [Fact]
    public void GatewayMicroservices_FlagsTheGatewayThroughPatternB()
    {
        // Post 4 of §18: nobody depends on a gateway, so only pattern B sees it.
        var result = ReferenceArchitecture.Analyze("gateway-microservices");

        Assert.Contains(result.Findings, f =>
            f.Code == RuleCodes.Spof &&
            f.Severity == Severity.Critical &&
            f.Message.Describe() == "finding.SPOF.unreachable.message(count=4, node=Gateway)");
    }

    [Fact]
    public void GatewayMicroservices_GradesSpofByBlastRadius()
    {
        // Db with three dependents is Critical; Payments with two is a Warning. Without the
        // grading, MISSING_RESILIENCE would escalate towards both and fill the panel.
        var result = ReferenceArchitecture.Analyze("gateway-microservices");
        var spofs = result.Findings.Where(f => f.Code == RuleCodes.Spof).ToList();

        Assert.Contains(spofs, f => f.Severity == Severity.Critical && f.Message.Arg("node") == "Db");
        Assert.Contains(spofs, f => f.Severity == Severity.Warning && f.Message.Arg("node") == "Payments");
    }

    [Fact]
    public void ResilientAsync_ScoresNinetyFive()
    {
        var result = ReferenceArchitecture.Analyze("resilient-async");

        // **92 in v4 and 93 in v5, and the architecture did not change.** V13 D1.1: SPOF used to
        // count three of them here - Db, Queue and Cache - by counting arrows without reading them.
        // The Queue only ever receives asynchronous posts and the Cache is called with a declared
        // fallback on both ways in, so neither is a component anything falls with. Availability
        // pays 5 instead of 10 and the architecture the catalogue ships as *the good one* stops
        // being scolded for the two mechanisms that make it good.
        Assert.Equal(95, result.ScoreOf(Area.Availability));

        // 95 in v2, 90 while SYNC_CHAIN counted boxes, 95 again since V14 B3. QUEUE_NO_DLQ fires
        // here and nowhere else - it is the only fixture with a queue, and the field it reads came
        // back in the same commit as the rule (D5.2). What left is the chain: `LB -> Api -> Cache
        // -> Db` was read as four links and it is one API reaching its own cache and store, which
        // is not a chain of anything.
        Assert.Equal(95, result.ScoreOf(Area.Resilience));

        Assert.Equal(100, result.ScoreOf(Area.Coupling));
        Assert.Equal(100, result.ScoreOf(Area.Scalability));

        // Both APIs sit behind the balancer with nothing in front that could authenticate, so both
        // are public - and neither declared anything, so it is 5 for the first and 2,5 for the
        // second (V13 D2.1 over D1.1's diminishing returns). It was 78 while an undeclared field
        // counted as a decision.
        Assert.Equal(93, result.ScoreOf(Area.Security));

        // Seven components and no telemetry: the cap (QA §4, coverage). "Resilient" was never
        // "observed", and the fixture that does everything else right still runs blind.
        Assert.Equal(85, result.ScoreOf(Area.Observability));

        // 94 until V14 B3 took the chain off it. `LB -> Api -> Cache -> Db` is not four links, it
        // is one API and the two things it stores in.
        Assert.Equal(95, result.Score);

        // **Not one Critical left on the architecture the catalogue ships as the good one.** V13
        // takes two SPOFs off it (D1.1) and downgrades the two entry points (D2.1); what remains is
        // warnings, every one of them a real thing to go and look at - the five of before, and
        // since QA §4 one per box nobody watches.
        //
        // And three more green ticks since QA §4, all three earned: the queue has its worker, the
        // cache has the database behind it, and Orders is the only service on that database.
        // V14 B3 turns SYNC_CHAIN's Warning into its green tick here too.
        Assert.Equal(new AnalysisSummary(Critical: 0, Warning: 11, Passed: 12), result.Summary);
    }

    [Fact]
    public void ResilientAsync_KeepsTheDatabaseAndDropsTheQueueAndTheCache()
    {
        // V13 D1.1, and the reason this is not simply "trust the fallback". The Db is called with a
        // circuit breaker **and** a fallback on both of its incoming edges, exactly like the Cache -
        // and it is still the one SPOF left, because it is the system of record: a fallback in front
        // of it serves something stale or refuses, and neither of those is the write that was lost.
        //
        // Three protected components coming out clean would have been the opposite failure to the
        // one V13 fixes.
        var result = ReferenceArchitecture.Analyze("resilient-async");
        var spofs = result.Findings.Where(f => f.Code == RuleCodes.Spof).ToList();

        Assert.Equal(["Db"], spofs.Select(f => f.Message.Arg("node")));
        Assert.Equal(Severity.Warning, Assert.Single(spofs).Severity);
    }

    [Fact]
    public void ResilientAsync_TellsTheDatabaseWhatToDoAboutIt()
    {
        // V13 D1.2. The suggestion is the database's, not the one constant every SPOF used to get.
        var result = ReferenceArchitecture.Analyze("resilient-async");

        Assert.Equal(
            "finding.SPOF.suggestion.database",
            Assert.Single(result.Findings, f => f.Code == RuleCodes.Spof).Suggestion.Key());
    }

    [Fact]
    public void ResilientAsync_NeverFlagsTheLoadBalancer()
    {
        // If the LB came out flagged, the model would be in a loop: you add a balancer to fix a
        // SPOF and the balancer becomes the SPOF.
        var result = ReferenceArchitecture.Analyze("resilient-async");

        Assert.DoesNotContain(result.Findings, f => f.Code == RuleCodes.Spof && f.Message.Arg("node") == "LB");
    }

    [Fact]
    public void ResilientAsync_DoesNotCountTheCacheAsAConsumerOfItself()
    {
        // Cache -> Db plus Orders -> Db leaves one effective consumer, below the threshold.
        var result = ReferenceArchitecture.Analyze("resilient-async");

        Assert.Contains(result.Findings, f => f.Code == RuleCodes.MissingCache && f.Severity == Severity.Good);
    }

    [Fact]
    public void ResilientAsync_CircuitBreakerAndFallbackPreventTheEscalation()
    {
        var result = ReferenceArchitecture.Analyze("resilient-async");

        Assert.Contains(result.Findings, f => f.Code == RuleCodes.MissingResilience && f.Severity == Severity.Good);
    }

    [Fact]
    public void WorstCase_ScoresSeventyFour()
    {
        // The fixture that pins the caps down - MISSING_RESILIENCE fires six times and only costs
        // 45 - and the only one where the rounding of an area matters: without rounding
        // Availability to 68 before the average, the result would be 64.
        var result = ReferenceArchitecture.Analyze("worst-case");

        Assert.Equal(68, result.ScoreOf(Area.Availability));

        // 25 until V14 B3, when SYNC_CHAIN stopped counting the gateway and the database as links:
        // the chain here is `Orders -> Shipping -> Notifications`, three services, a Warning - not
        // the Critical of five boxes it used to report from the Gateway.
        Assert.Equal(35, result.ScoreOf(Area.Resilience));

        // 70 until QA §4: a Critical cycle and a Critical fan-out, and now the Warning for four
        // services - Orders, Notifications, Users, Billing - meeting on one database.
        Assert.Equal(65, result.ScoreOf(Area.Coupling));
        Assert.Equal(95, result.ScoreOf(Area.Scalability));
        Assert.Equal(95, result.ScoreOf(Area.Security));
        Assert.Equal(85, result.ScoreOf(Area.Observability));   // ten boxes, nothing watching: the cap

        // **65 in v2, 73 in v4, 75 in v5, 72 in v6, 74 since V14 B3**, and it is the worst
        // architecture in the suite that gains the most. Six areas averaged without weights means
        // freshly measured ones at 95 pull a 25 upwards. Not a bug and not a calibration to fiddle
        // with (D1.2 chose the plain average on purpose) - a reason why comparing two scoring
        // versions is meaningless.
        Assert.Equal(74, result.Score);

        // What keeps this fixture honest: **eleven Criticals** - twelve until V14 B3 moved
        // SYNC_CHAIN's to the Warning column, which is the one place that fix softens this fixture,
        // and it softens it to the truth: three chained services, not five boxes. V13 lowers the
        // floor under the small correct architectures without softening the bad one - the two moves
        // are separate, and this is where you see it.
        Assert.Equal(11, result.Summary.Critical);
    }

    [Fact]
    public void WorstCase_FiresEverySevenV2RulesPlusTheTwoV4FindsHere()
    {
        var result = ReferenceArchitecture.Analyze("worst-case");

        Assert.Equal(
            [
                RuleCodes.Cycle, RuleCodes.HighCoupling, RuleCodes.MissingCache,
                RuleCodes.MissingResilience, RuleCodes.NoObservability, RuleCodes.RetryAmplification,
                RuleCodes.SharedDatabase, RuleCodes.Spof, RuleCodes.SyncChain, RuleCodes.UnauthenticatedEntry
            ],
            result.Findings.Where(f => f.Severity != Severity.Good).Select(f => f.Code).Distinct().Order());

        // Three of the eight new rules looked here and found nothing - no orphans, no timeout
        // shorter than its own call, no database hanging in the open - and saying so is what keeps
        // a passed check a check (D1.4). The other three were not applicable: no queue, and no
        // declared traffic for the two rules that need it. Of the three of QA §4, one fires (four
        // services on Db) and two have nothing to look at - no queue, no cache.
        //
        // Before V4 this fixture had **zero** passed checks. Even the worst architecture in the
        // suite does four things right; nobody was asking. The fourth is V13 D3.1: every one of its
        // fourteen edges names a protocol that matches what is at the other end, which is worth a
        // green tick precisely because the rule exists to catch the ones that do not.
        Assert.Equal(4, result.Summary.Passed);
    }

    [Theory]
    [InlineData("minimal-api-db")]
    [InlineData("worst-case")]
    [InlineData("resilient-async")]
    [InlineData("gateway-microservices")]
    public void ShufflingTheInput_DoesNotChangeTheAnalysis(string fixtureName)
    {
        // V6 D1.1, and the test this project went four versions without. The simulator declared
        // this property in V3 and tested it; the analyzer never did, and it was not true: two
        // identical requests could return the same findings in a different order, because the
        // repository reads without an OrderBy and the tie-break preserved whatever came out.
        //
        // Reversing is enough and is worth more than a random shuffle: it is reproducible, and it
        // is the worst case for an order that used to be inherited.
        var (nodes, connections) = ReferenceArchitecture.Load(fixtureName);

        var straight = ReferenceArchitecture.Analyze(new ArchitectureGraph(nodes, connections));
        var reversed = ReferenceArchitecture.Analyze(new ArchitectureGraph(
            [.. Enumerable.Reverse(nodes)],
            [.. Enumerable.Reverse(connections)]));

        Assert.Equal(Render(straight), Render(reversed));
    }

    [Fact]
    public void WorstCase_BreaksTiesByTheOrderOfTheGraph()
    {
        // The fourth tie-break where it is worth reading: three SPOFs of the same severity, and
        // the order is the one the export lists the components in - Db (Database) before Orders
        // (Service) before Gateway (Gateway), by declaration order of NodeType and then by name.
        var result = ReferenceArchitecture.Analyze("worst-case");

        var graph = ReferenceArchitecture.Graph("worst-case");

        Assert.Equal(
            ["Db", "Orders", "Gateway", "Notifications"],
            result.Findings
                .Where(f => f.Code == RuleCodes.Spof)
                .Select(f => graph.NodesById[f.Elements[0].Id].Name));
    }

    [Fact]
    public void WorstCase_OrdersFindingsBySeverityThenAreaThenCode()
    {
        // D2.1, rule 4. This is the fixture with enough findings for the order to be worth
        // asserting whole.
        var result = ReferenceArchitecture.Analyze("worst-case");

        Assert.Equal(
            [
                // Critical: Availability, then Resilience by code, then Coupling by code.
                RuleCodes.Spof, RuleCodes.Spof, RuleCodes.Spof,
                RuleCodes.MissingResilience, RuleCodes.MissingResilience, RuleCodes.MissingResilience,
                RuleCodes.MissingResilience, RuleCodes.MissingResilience,
                RuleCodes.RetryAmplification,
                RuleCodes.Cycle, RuleCodes.HighCoupling,
                // Warning: same walk through the areas. UNAUTHENTICATED_ENTRY crossed over from the
                // block above in V13 D2.1, and lands under Security exactly where the order says.
                // SYNC_CHAIN moved down here in V14 B3: three services chained is a Warning, and
                // the Critical of five was four boxes of infrastructure padding the count.
                RuleCodes.Spof, RuleCodes.MissingResilience, RuleCodes.SyncChain,
                RuleCodes.SharedDatabase, RuleCodes.MissingCache,
                RuleCodes.UnauthenticatedEntry,
                // Coverage (QA §4): one per box nobody watches, and this fixture has ten.
                RuleCodes.NoObservability, RuleCodes.NoObservability, RuleCodes.NoObservability,
                RuleCodes.NoObservability, RuleCodes.NoObservability, RuleCodes.NoObservability,
                RuleCodes.NoObservability, RuleCodes.NoObservability, RuleCodes.NoObservability,
                RuleCodes.NoObservability
            ],
            result.Findings.Where(f => f.Severity != Severity.Good).Select(f => f.Code));
    }

    [Fact]
    public void WorstCase_CapsMissingResilienceAtThreeTimesItsWeight()
    {
        // Six findings would be 52.5 points; the cap leaves 45, which is what keeps Resilience off
        // the floor - 25 while SYNC_CHAIN added a Critical of its own, 35 since V14 B3 dropped it
        // to the Warning the three chained services earn.
        var result = ReferenceArchitecture.Analyze("worst-case");

        Assert.Equal(6, result.Findings.Count(f => f.Code == RuleCodes.MissingResilience));
        Assert.Equal(35, result.ScoreOf(Area.Resilience));
    }

    [Theory]
    [InlineData("minimal-api-db")]
    [InlineData("gateway-microservices")]
    [InlineData("resilient-async")]
    [InlineData("worst-case")]
    public void EveryFixture_CarriesTheSixAreasAndTheScoringVersion(string fixture)
    {
        var result = ReferenceArchitecture.Analyze(fixture);

        Assert.Equal("v6", result.ScoringVersion);
        Assert.Equal(Enum.GetValues<Area>(), result.Areas.Select(a => a.Area));
        Assert.All(result.Findings, finding =>
        {
            // D2.1, rule 2: an explanation is mandatory on every finding, a suggestion is null
            // exactly on the Good ones.
            // D2.1, rule 2 becomes a check on the contract rather than on prose: a finding names a
            // sentence, and a Good one names no suggestion.
            Assert.StartsWith("finding.", finding.Message.Key());
            Assert.StartsWith("finding.", finding.Explanation.Key());
            Assert.Equal(finding.Severity == Severity.Good, finding.Suggestion is null);
            Assert.NotNull(finding.Elements);
        });
    }

    /// <summary>
    /// The whole result as text, so the shuffling test compares everything at once instead of the
    /// three fields somebody remembered to assert: score, areas, summary and every finding with the
    /// elements it points at, in order.
    /// </summary>
    private static string Render(AnalysisResult result)
    {
        var lines = new List<string>
        {
            $"score={result.Score} version={result.ScoringVersion}",
            $"summary={result.Summary.Critical}/{result.Summary.Warning}/{result.Summary.Passed}"
        };

        lines.AddRange(result.Areas.Select(a => $"area {a.Area} {a.State} {a.Score}"));
        lines.AddRange(result.Findings.Select(f =>
            $"{f.Severity} {f.Area} {f.Code} | {f.Message.Describe()} | " +
            string.Join(",", f.Elements.Select(e => $"{e.Type}:{e.Id}"))));

        return string.Join("\n", lines);
    }
}
