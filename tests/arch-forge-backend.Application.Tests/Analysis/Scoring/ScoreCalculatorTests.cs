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
        var result = Calculate(Outcome(Area.Availability, RuleCodes.Spof,
            Severity.Critical, Severity.Critical, Severity.Critical));

        Assert.Equal(70, ScoreOf(result, Area.Availability));
    }

    [Fact]
    public void Calculate_CapsPenaltyAtThreeTimesTheBaseWeight()
    {
        var result = Calculate(Outcome(Area.Resilience, RuleCodes.MissingResilience,
            Enumerable.Repeat(Severity.Critical, 6).ToArray()));

        Assert.Equal(55, ScoreOf(result, Area.Resilience));
    }

    [Fact]
    public void Calculate_CapIsPerRuleCodeNotPerArea()
    {
        var result = Calculate(
            Outcome(Area.Resilience, RuleCodes.SyncChain, Severity.Critical),
            Outcome(Area.Resilience, RuleCodes.RetryAmplification, Severity.Critical),
            Outcome(Area.Resilience, RuleCodes.MissingResilience,
                Enumerable.Repeat(Severity.Critical, 6).ToArray()));

        Assert.Equal(25, ScoreOf(result, Area.Resilience));
    }

    [Fact]
    public void Calculate_MixedSeverities_PenalizesWorstFirst()
    {
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
        var result = Calculate(Outcome(Area.Availability, RuleCodes.Spof, Severity.Critical));

        Assert.Equal(new AreaScore(Area.Security, null, AreaState.NoRules), AreaOf(result, Area.Security));
        Assert.Equal(new AreaScore(Area.Observability, null, AreaState.NoRules), AreaOf(result, Area.Observability));

        // The area itself still scores 85; what there is no score for is the architecture, because
        // one axis out of seven is not an average of anything (V22 D1).
        Assert.Equal(85, ScoreOf(result, Area.Availability));
        Assert.Null(result.Score);
    }

    [Fact]
    public void Calculate_AreaWhoseRulesNeverApplied_IsNotApplicable()
    {
        var result = ScoreCalculator.Calculate([
            new RuleOutcome(Area.Availability, RuleCodes.Spof, IsApplicable: false, []),
            new RuleOutcome(Area.Coupling, RuleCodes.HighCoupling, IsApplicable: true, [])
        ]);

        Assert.Equal(AreaState.NotApplicable, AreaOf(result, Area.Availability).State);
        Assert.Equal(100, ScoreOf(result, Area.Coupling));
        Assert.Null(result.Score);
    }

    // V22 D1. Six unconnected boxes used to score 100 / 100: six of the seven areas had nothing to
    // look at, so the number was the average of the one that could speak, and it had no complaint.
    [Fact]
    public void Calculate_FewerThanHalfTheAreasScored_HasNoScoreAtAll()
    {
        var result = ScoreCalculator.Calculate([
            Outcome(Area.Coupling, RuleCodes.OrphanNode, Severity.Info),
            Outcome(Area.Availability, RuleCodes.Spof, Severity.Good),
        ]);

        Assert.Equal(100, ScoreOf(result, Area.Coupling));
        Assert.Equal(100, ScoreOf(result, Area.Availability));
        Assert.Null(result.Score);
    }

    [Fact]
    public void Calculate_HalfTheAreasScored_AveragesThem()
    {
        var result = ScoreCalculator.Calculate([
            Outcome(Area.Availability, RuleCodes.Spof, Severity.Critical),
            Outcome(Area.Coupling, RuleCodes.Cycle, Severity.Good),
            Outcome(Area.Security, RuleCodes.ExposedDatabase, Severity.Good),
            Outcome(Area.Resilience, RuleCodes.SyncChain, Severity.Good),
        ]);

        // 85 + 100 + 100 + 100, over four.
        Assert.Equal(96, result.Score);
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
        var result = ScoreCalculator.Calculate([]);

        Assert.Equal(Enum.GetValues<Area>(), result.Areas.Select(a => a.Area));
    }

    [Fact]
    public void Calculate_WritesTheArithmeticOutPerCode()
    {
        var result = Calculate(
            Outcome(Area.Resilience, RuleCodes.SyncChain, Severity.Warning),
            Outcome(Area.Resilience, RuleCodes.RetryAmplification, Severity.Good),
            Outcome(Area.Resilience, RuleCodes.MissingResilience,
                Enumerable.Repeat(Severity.Critical, 6).ToArray()));

        var area = AreaOf(result, Area.Resilience);

        Assert.Equal(50, area.Score);
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
