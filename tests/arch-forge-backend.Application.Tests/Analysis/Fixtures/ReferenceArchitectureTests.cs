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
        // still perfect; the three points it loses are an unauthenticated front door with a database
        // behind it, and nothing watching. Both are true of these two boxes, and until V4 nobody
        // was asking.
        var result = ReferenceArchitecture.Analyze("minimal-api-db");

        Assert.Equal(100, result.ScoreOf(Area.Availability));
        Assert.Equal(100, result.ScoreOf(Area.Resilience));
        Assert.Equal(100, result.ScoreOf(Area.Coupling));
        Assert.Equal(100, result.ScoreOf(Area.Scalability));

        Assert.Equal(85, result.ScoreOf(Area.Security));        // UNAUTHENTICATED_ENTRY, critical
        Assert.Equal(95, result.ScoreOf(Area.Observability));   // NO_OBSERVABILITY, warning
        Assert.Equal(97, result.Score);                         // (100·4 + 85 + 95) / 6
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
        Assert.Equal(60, result.ScoreOf(Area.Resilience));
        Assert.Equal(100, result.ScoreOf(Area.Coupling));
        Assert.Equal(95, result.ScoreOf(Area.Scalability));
        Assert.Equal(85, result.ScoreOf(Area.Security));
        Assert.Equal(95, result.ScoreOf(Area.Observability));

        // **83 in v2 and 85 in v4, and the architecture did not change.** Two areas that were not
        // measured are now measured and only mildly penalised, so an unweighted average over six
        // dilutes the 60 and the 75 it already had. Scores from two scoring versions are not
        // comparable, which is what `scoringVersion` is for (V2 D1.5) and why a challenge may never
        // assert on one (D1.4).
        Assert.Equal(85, result.Score);
        // V2 counted 5 · 4 · 2. V4 adds one critical (the unauthenticated gateway), one warning
        // (nothing watching) and three passed checks - the rules that looked and found nothing.
        Assert.Equal(new AnalysisSummary(Critical: 6, Warning: 5, Passed: 5), result.Summary);
    }

    [Fact]
    public void GatewayMicroservices_FlagsTheGatewayThroughPatternB()
    {
        // Post 4 of §18: nobody depends on a gateway, so only pattern B sees it.
        var result = ReferenceArchitecture.Analyze("gateway-microservices");

        Assert.Contains(result.Findings, f =>
            f.Code == RuleCodes.Spof &&
            f.Severity == Severity.Critical &&
            f.Message == "'Gateway' es un punto único de fallo: 4 componentes quedan inalcanzables si deja de funcionar.");
    }

    [Fact]
    public void GatewayMicroservices_GradesSpofByBlastRadius()
    {
        // Db with three dependents is Critical; Payments with two is a Warning. Without the
        // grading, MISSING_RESILIENCE would escalate towards both and fill the panel.
        var result = ReferenceArchitecture.Analyze("gateway-microservices");
        var spofs = result.Findings.Where(f => f.Code == RuleCodes.Spof).ToList();

        Assert.Contains(spofs, f => f.Severity == Severity.Critical && f.Message.Contains("'Db'"));
        Assert.Contains(spofs, f => f.Severity == Severity.Warning && f.Message.Contains("'Payments'"));
    }

    [Fact]
    public void ResilientAsync_ScoresNinetyTwo()
    {
        var result = ReferenceArchitecture.Analyze("resilient-async");

        Assert.Equal(90, result.ScoreOf(Area.Availability));

        // 95 in v2. QUEUE_NO_DLQ fires here and nowhere else - it is the only fixture with a queue,
        // and the field it reads came back in the same commit as the rule (D5.2).
        Assert.Equal(90, result.ScoreOf(Area.Resilience));

        Assert.Equal(100, result.ScoreOf(Area.Coupling));
        Assert.Equal(100, result.ScoreOf(Area.Scalability));

        // Both APIs sit behind the balancer with nothing in front that could authenticate, so both
        // are public: 15 for the first and 7,5 for the second (D1.1's diminishing returns).
        Assert.Equal(78, result.ScoreOf(Area.Security));
        Assert.Equal(95, result.ScoreOf(Area.Observability));
        Assert.Equal(92, result.Score);
    }

    [Fact]
    public void ResilientAsync_NeverFlagsTheLoadBalancer()
    {
        // If the LB came out flagged, the model would be in a loop: you add a balancer to fix a
        // SPOF and the balancer becomes the SPOF.
        var result = ReferenceArchitecture.Analyze("resilient-async");

        Assert.DoesNotContain(result.Findings, f => f.Code == RuleCodes.Spof && f.Message.Contains("'LB'"));
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
    public void WorstCase_ScoresSeventyThree()
    {
        // The fixture that pins the caps down - MISSING_RESILIENCE fires six times and only costs
        // 45 - and the only one where the rounding of an area matters: without rounding
        // Availability to 68 before the average, the result would be 64.
        var result = ReferenceArchitecture.Analyze("worst-case");

        Assert.Equal(68, result.ScoreOf(Area.Availability));
        Assert.Equal(25, result.ScoreOf(Area.Resilience));
        Assert.Equal(70, result.ScoreOf(Area.Coupling));
        Assert.Equal(95, result.ScoreOf(Area.Scalability));
        Assert.Equal(85, result.ScoreOf(Area.Security));
        Assert.Equal(95, result.ScoreOf(Area.Observability));

        // **65 in v2 and 73 in v4**, and it is the worst architecture in the suite that gains the
        // most. Six areas averaged without weights means two freshly measured ones at 85 and 95
        // pull a 25 upwards. Not a bug and not a calibration to fiddle with (D1.2 chose the plain
        // average on purpose) - a reason why comparing two scoring versions is meaningless.
        Assert.Equal(73, result.Score);
    }

    [Fact]
    public void WorstCase_FiresEverySevenV2RulesPlusTheTwoV4FindsHere()
    {
        var result = ReferenceArchitecture.Analyze("worst-case");

        Assert.Equal(
            [
                RuleCodes.Cycle, RuleCodes.HighCoupling, RuleCodes.MissingCache,
                RuleCodes.MissingResilience, RuleCodes.NoObservability, RuleCodes.RetryAmplification,
                RuleCodes.Spof, RuleCodes.SyncChain, RuleCodes.UnauthenticatedEntry
            ],
            result.Findings.Where(f => f.Severity != Severity.Good).Select(f => f.Code).Distinct().Order());

        // Three of the eight new rules looked here and found nothing - no orphans, no timeout
        // shorter than its own call, no database hanging in the open - and saying so is what keeps
        // a passed check a check (D1.4). The other three were not applicable: no queue, and no
        // declared traffic for the two rules that need it.
        //
        // Before V4 this fixture had **zero** passed checks. Even the worst architecture in the
        // suite does three things right; nobody was asking.
        Assert.Equal(3, result.Summary.Passed);
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
                RuleCodes.RetryAmplification, RuleCodes.SyncChain,
                RuleCodes.Cycle, RuleCodes.HighCoupling,
                RuleCodes.UnauthenticatedEntry,
                // Warning: same walk through the areas.
                RuleCodes.Spof, RuleCodes.MissingResilience, RuleCodes.MissingCache,
                RuleCodes.NoObservability
            ],
            result.Findings.Where(f => f.Severity != Severity.Good).Select(f => f.Code));
    }

    [Fact]
    public void WorstCase_CapsMissingResilienceAtThreeTimesItsWeight()
    {
        // Six findings would be 52.5 points; the cap leaves 45, which is what keeps Resilience at
        // 25 instead of bottoming out.
        var result = ReferenceArchitecture.Analyze("worst-case");

        Assert.Equal(6, result.Findings.Count(f => f.Code == RuleCodes.MissingResilience));
        Assert.Equal(25, result.ScoreOf(Area.Resilience));
    }

    [Theory]
    [InlineData("minimal-api-db")]
    [InlineData("gateway-microservices")]
    [InlineData("resilient-async")]
    [InlineData("worst-case")]
    public void EveryFixture_CarriesTheSixAreasAndTheScoringVersion(string fixture)
    {
        var result = ReferenceArchitecture.Analyze(fixture);

        Assert.Equal("v4", result.ScoringVersion);
        Assert.Equal(Enum.GetValues<Area>(), result.Areas.Select(a => a.Area));
        Assert.All(result.Findings, finding =>
        {
            // D2.1, rule 2: an explanation is mandatory on every finding, a suggestion is null
            // exactly on the Good ones.
            Assert.False(string.IsNullOrWhiteSpace(finding.Message));
            Assert.False(string.IsNullOrWhiteSpace(finding.Explanation));
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
            $"{f.Severity} {f.Area} {f.Code} | {f.Message} | " +
            string.Join(",", f.Elements.Select(e => $"{e.Type}:{e.Id}"))));

        return string.Join("\n", lines);
    }
}
