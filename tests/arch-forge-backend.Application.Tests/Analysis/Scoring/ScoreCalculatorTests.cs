using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Scoring;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Scoring;

public class ScoreCalculatorTests
{
    [Fact]
    public void Calculate_FirstFindingOfACodeCostsFullWeightAndTheRestHalf()
    {
        // D1.1: 100 - 15 - 7.5 - 7.5 = 70, the worked example of the decisions document.
        var result = Calculate(Outcome(Area.Availability, RuleCodes.Spof,
            Severity.Critical, Severity.Critical, Severity.Critical));

        Assert.Equal(70, ScoreOf(result, Area.Availability));
    }

    [Fact]
    public void Calculate_CapsPenaltyAtThreeTimesTheBaseWeight()
    {
        // Six criticals would be 52.5; the cap leaves 45. Without a cap a big architecture with
        // many findings would lose more than the area has to give (D1.1).
        var result = Calculate(Outcome(Area.Resilience, RuleCodes.MissingResilience,
            Enumerable.Repeat(Severity.Critical, 6).ToArray()));

        Assert.Equal(55, ScoreOf(result, Area.Resilience));
    }

    [Fact]
    public void Calculate_CapIsPerRuleCodeNotPerArea()
    {
        // Resilience is fed by three rules; each one caps on its own, which is what lets an area
        // actually reach the floor.
        var result = Calculate(
            Outcome(Area.Resilience, RuleCodes.SyncChain, Severity.Critical),
            Outcome(Area.Resilience, RuleCodes.RetryAmplification, Severity.Critical),
            Outcome(Area.Resilience, RuleCodes.MissingResilience,
                Enumerable.Repeat(Severity.Critical, 6).ToArray()));

        // 100 - 15 - 15 - 45 = 25, the Resilience of worst-case.json.
        Assert.Equal(25, ScoreOf(result, Area.Resilience));
    }

    [Fact]
    public void Calculate_MixedSeverities_PenalizesWorstFirst()
    {
        // D1.1: descending severity, first at full weight. 100 - 15 - 7.5 - 2.5 = 75, the
        // Availability of gateway-microservices.json.
        var result = Calculate(Outcome(Area.Availability, RuleCodes.Spof,
            Severity.Warning, Severity.Critical, Severity.Critical));

        Assert.Equal(75, ScoreOf(result, Area.Availability));
    }

    [Fact]
    public void Calculate_GoodFindingsCostNothing()
    {
        var result = Calculate(Outcome(Area.Coupling, RuleCodes.HighCoupling,
            Severity.Good, Severity.Good));

        Assert.Equal(100, ScoreOf(result, Area.Coupling));
    }

    [Fact]
    public void Calculate_ClampsAreaAtZero()
    {
        var result = Calculate(
            Outcome(Area.Resilience, RuleCodes.SyncChain, Enumerable.Repeat(Severity.Critical, 6).ToArray()),
            Outcome(Area.Resilience, RuleCodes.MissingResilience, Enumerable.Repeat(Severity.Critical, 6).ToArray()),
            Outcome(Area.Resilience, RuleCodes.RetryAmplification, Enumerable.Repeat(Severity.Critical, 6).ToArray()));

        Assert.Equal(0, ScoreOf(result, Area.Resilience));
    }

    [Fact]
    public void Calculate_RoundsEachAreaBeforeAveraging()
    {
        // The four numbers the user sees are the ones that add up to the big one (D1.1). These are
        // the areas of worst-case.json: Availability is 67.5, and averaging it unrounded would
        // give 64 instead of 65.
        var result = Calculate(
            Outcome(Area.Availability, RuleCodes.Spof,
                Severity.Critical, Severity.Critical, Severity.Critical, Severity.Warning),
            Outcome(Area.Resilience, RuleCodes.SyncChain, Severity.Critical),
            Outcome(Area.Resilience, RuleCodes.RetryAmplification, Severity.Critical),
            Outcome(Area.Resilience, RuleCodes.MissingResilience, Enumerable.Repeat(Severity.Critical, 6).ToArray()),
            Outcome(Area.Coupling, RuleCodes.HighCoupling, Severity.Critical),
            Outcome(Area.Coupling, RuleCodes.Cycle, Severity.Critical),
            Outcome(Area.Scalability, RuleCodes.MissingCache, Severity.Warning));

        Assert.Equal(68, ScoreOf(result, Area.Availability));
        Assert.Equal(25, ScoreOf(result, Area.Resilience));
        Assert.Equal(70, ScoreOf(result, Area.Coupling));
        Assert.Equal(95, ScoreOf(result, Area.Scalability));
        Assert.Equal(65, result.Score);
    }

    [Fact]
    public void Calculate_AreaWithNoRules_IsNoRulesAndStaysOutOfTheAverage()
    {
        // Security and Observability in V2: a 100 nobody checked is exactly the decorative number
        // D1.2 exists to avoid.
        var result = Calculate(Outcome(Area.Availability, RuleCodes.Spof, Severity.Critical));

        Assert.Equal(new AreaScore(Area.Security, null, AreaState.NoRules), AreaOf(result, Area.Security));
        Assert.Equal(new AreaScore(Area.Observability, null, AreaState.NoRules), AreaOf(result, Area.Observability));
        Assert.Equal(85, result.Score);
    }

    [Fact]
    public void Calculate_AreaWhoseRulesNeverApplied_IsNotApplicable()
    {
        var result = ScoreCalculator.Calculate([
            new RuleOutcome(Area.Availability, RuleCodes.Spof, IsApplicable: false, []),
            new RuleOutcome(Area.Coupling, RuleCodes.HighCoupling, IsApplicable: true, [])
        ]);

        Assert.Equal(AreaState.NotApplicable, AreaOf(result, Area.Availability).State);
        Assert.Equal(100, result.Score);
    }

    [Fact]
    public void Calculate_NoScorableArea_ReturnsNullScore()
    {
        var result = ScoreCalculator.Calculate([
            new RuleOutcome(Area.Availability, RuleCodes.Spof, IsApplicable: false, [])
        ]);

        Assert.Null(result.Score);
    }

    [Fact]
    public void Calculate_AlwaysReturnsTheSixAreasInEnumOrder()
    {
        // D2.1, rule 1: the six areas always travel, what varies is their state.
        var result = ScoreCalculator.Calculate([]);

        Assert.Equal(Enum.GetValues<Area>(), result.Areas.Select(a => a.Area));
    }

    [Fact]
    public void Calculate_WritesTheArithmeticOutPerCode()
    {
        // QA §4, "explica esta nota": the number was always reconstructible by hand from the
        // source; now the lines that add up to it travel with it, one per code that cost something,
        // in code order, with the cap named when it is what the number is.
        var result = Calculate(
            Outcome(Area.Resilience, RuleCodes.SyncChain, Severity.Warning),
            Outcome(Area.Resilience, RuleCodes.RetryAmplification, Severity.Good),
            Outcome(Area.Resilience, RuleCodes.MissingResilience,
                Enumerable.Repeat(Severity.Critical, 6).ToArray()));

        var area = AreaOf(result, Area.Resilience);

        Assert.Equal(50, area.Score);   // 100 - 45 - 5
        Assert.Equal(
            [
                new PenaltyLine(RuleCodes.MissingResilience, Severity.Critical, Findings: 6, Penalty: 45m, Capped: true),
                new PenaltyLine(RuleCodes.SyncChain, Severity.Warning, Findings: 1, Penalty: 5m, Capped: false)
            ],
            area.Penalties);
    }

    [Fact]
    public void Calculate_APerfectOrUnscoredArea_HasNoLines()
    {
        var result = Calculate(Outcome(Area.Coupling, RuleCodes.Cycle, Severity.Good));

        Assert.Empty(AreaOf(result, Area.Coupling).Penalties);
        Assert.Empty(AreaOf(result, Area.Security).Penalties);
    }

    private static ScoreResult Calculate(params RuleOutcome[] outcomes) => ScoreCalculator.Calculate(outcomes);

    private static RuleOutcome Outcome(Area area, string code, params Severity[] severities) =>
        new(area, code, IsApplicable: true, severities
            .Select(severity => new RuleFinding(code, area, severity, LocalizedText.Of("message"), LocalizedText.Of("explanation"), null, []))
            .ToList());

    private static AreaScore AreaOf(ScoreResult result, Area area) => result.Areas.Single(a => a.Area == area);

    private static int? ScoreOf(ScoreResult result, Area area) => AreaOf(result, area).Score;
}
