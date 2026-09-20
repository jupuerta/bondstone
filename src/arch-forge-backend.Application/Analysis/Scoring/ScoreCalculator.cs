namespace bondstone_backend.Application.Analysis.Scoring;

public sealed record RuleOutcome(
    Area Area,
    string Code,
    bool IsApplicable,
    IReadOnlyList<RuleFinding> Findings,
    Localization.LocalizedText? NotApplicableReason = null,
    bool DescribesTheDrawing = false);

public sealed record ScoreResult(int? Score, IReadOnlyList<AreaScore> Areas);

public static class ScoreCalculator
{
    private const decimal CriticalWeight = 15m;
    private const decimal WarningWeight = 5m;

    private const decimal RepeatFactor = 0.5m;

    private const int CapMultiplier = 3;

    // V22 D1. How many of the seven areas have to be evaluable before the average of them is worth
    // calling a score.
    //
    // Measured: six unconnected boxes scored **100 / 100**. Six of the seven areas had nothing to
    // look at — no traffic path, no calls, no store — so the number was the average of the one area
    // that could speak, and it had nothing to complain about. A canvas that is not an architecture
    // yet was being told it was perfect.
    //
    // Four is half of seven rounded up, and it is comfortably clear of the real cases: two boxes and
    // an arrow already score six of seven areas, and the four references score six. What it catches
    // is drawings that have not started.
    private const int MinScoredAreas = 4;

    public static ScoreResult Calculate(IReadOnlyList<RuleOutcome> outcomes)
    {
        var areas = new List<AreaScore>();
        var scored = new List<int>();

        foreach (var area in Enum.GetValues<Area>())
        {
            var areaOutcomes = outcomes.Where(o => o.Area == area).ToList();

            if (areaOutcomes.Count == 0)
            {
                areas.Add(new AreaScore(area, null, AreaState.NoRules));
                continue;
            }

            if (!areaOutcomes.Any(o => o.IsApplicable))
            {
                areas.Add(new AreaScore(area, null, AreaState.NotApplicable));
                continue;
            }

            var lines = areaOutcomes
                .GroupBy(o => o.Code)
                .OrderBy(byCode => byCode.Key, StringComparer.Ordinal)
                .Select(byCode => PenaltyForCode(byCode.Key, byCode.SelectMany(o => o.Findings).ToList()))
                .OfType<PenaltyLine>()
                .ToList();

            var penalty = lines.Sum(line => line.Penalty);

            var areaScore = RoundHalfUp(Math.Max(0m, 100m - penalty));
            areas.Add(new AreaScore(area, areaScore, AreaState.Scored, lines));
            scored.Add(areaScore);
        }

        int? score = scored.Count < MinScoredAreas ? null : RoundHalfUp(scored.Sum() / (decimal)scored.Count);

        return new ScoreResult(score, areas);
    }

    private static PenaltyLine? PenaltyForCode(string code, IReadOnlyList<RuleFinding> findings)
    {
        var penalized = findings
            .Where(f => f.Severity is Severity.Warning or Severity.Critical)
            .OrderByDescending(f => f.Severity)
            .ToList();

        if (penalized.Count == 0)
            return null;

        var total = 0m;
        for (var i = 0; i < penalized.Count; i++)
        {
            var weight = WeightOf(penalized[i].Severity);
            total += i == 0 ? weight : weight * RepeatFactor;
        }

        var cap = WeightOf(penalized[0].Severity) * CapMultiplier;
        var capped = total > cap;

        return new PenaltyLine(code, penalized[0].Severity, penalized.Count, capped ? cap : total, capped);
    }

    private static decimal WeightOf(Severity severity) => severity switch
    {
        Severity.Critical => CriticalWeight,
        Severity.Warning => WarningWeight,
        _ => 0m
    };

    private static int RoundHalfUp(decimal value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
