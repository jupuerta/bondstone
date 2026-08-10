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
    public void MinimalApiDb_ScoresOneHundred()
    {
        // The fixture that proves a correct architecture scores 100. Without it, nothing stops the
        // analyzer from drifting into scolding the smallest correct thing you can draw.
        var result = ReferenceArchitecture.Analyze("minimal-api-db");

        Assert.Equal(100, result.Score);
        Assert.Equal(100, result.ScoreOf(Area.Availability));
        Assert.Equal(100, result.ScoreOf(Area.Resilience));
        Assert.Equal(100, result.ScoreOf(Area.Coupling));
        Assert.Equal(100, result.ScoreOf(Area.Scalability));
        Assert.Equal(new AnalysisSummary(Critical: 0, Warning: 0, Passed: 6), result.Summary);
    }

    [Fact]
    public void MinimalApiDb_LeavesTheTwoAreasWithoutRulesOutOfTheAverage()
    {
        // A 100 in Security nobody checked is the decorative number D1.2 exists to avoid.
        var result = ReferenceArchitecture.Analyze("minimal-api-db");

        Assert.Equal(AreaState.NoRules, result.StateOf(Area.Security));
        Assert.Equal(AreaState.NoRules, result.StateOf(Area.Observability));
        Assert.Null(result.ScoreOf(Area.Security));
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
    public void GatewayMicroservices_ScoresEightyThree()
    {
        // The most important fixture: a normal microservices architecture, neither exemplary nor a
        // disaster. (75 + 60 + 100 + 95) / 4 = 82.5 -> 83.
        var result = ReferenceArchitecture.Analyze("gateway-microservices");

        Assert.Equal(75, result.ScoreOf(Area.Availability));
        Assert.Equal(60, result.ScoreOf(Area.Resilience));
        Assert.Equal(100, result.ScoreOf(Area.Coupling));
        Assert.Equal(95, result.ScoreOf(Area.Scalability));
        Assert.Equal(83, result.Score);
        Assert.Equal(new AnalysisSummary(Critical: 5, Warning: 4, Passed: 2), result.Summary);
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
    public void ResilientAsync_ScoresNinetySix()
    {
        var result = ReferenceArchitecture.Analyze("resilient-async");

        Assert.Equal(90, result.ScoreOf(Area.Availability));
        Assert.Equal(95, result.ScoreOf(Area.Resilience));
        Assert.Equal(100, result.ScoreOf(Area.Coupling));
        Assert.Equal(100, result.ScoreOf(Area.Scalability));
        Assert.Equal(96, result.Score);
        Assert.Equal(new AnalysisSummary(Critical: 0, Warning: 4, Passed: 4), result.Summary);
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
    public void WorstCase_ScoresSixtyFive()
    {
        // The fixture that pins the caps down - MISSING_RESILIENCE fires six times and only costs
        // 45 - and the only one where the rounding of an area matters: without rounding
        // Availability to 68 before the average, the result would be 64.
        var result = ReferenceArchitecture.Analyze("worst-case");

        Assert.Equal(68, result.ScoreOf(Area.Availability));
        Assert.Equal(25, result.ScoreOf(Area.Resilience));
        Assert.Equal(70, result.ScoreOf(Area.Coupling));
        Assert.Equal(95, result.ScoreOf(Area.Scalability));
        Assert.Equal(65, result.Score);
    }

    [Fact]
    public void WorstCase_FiresAllSevenRules()
    {
        var result = ReferenceArchitecture.Analyze("worst-case");

        Assert.Equal(
            [
                RuleCodes.Cycle, RuleCodes.HighCoupling, RuleCodes.MissingCache,
                RuleCodes.MissingResilience, RuleCodes.RetryAmplification, RuleCodes.Spof,
                RuleCodes.SyncChain
            ],
            result.Findings.Select(f => f.Code).Distinct().Order());

        Assert.Equal(0, result.Summary.Passed);
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
                // Warning: same walk through the areas.
                RuleCodes.Spof, RuleCodes.MissingResilience, RuleCodes.MissingCache
            ],
            result.Findings.Select(f => f.Code));
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

        Assert.Equal("v2", result.ScoringVersion);
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
}
