namespace arch_forge_backend.Application.Analysis.Scoring;

public sealed record RuleOutcome(
    Area Area,
    string Code,
    bool IsApplicable,
    IReadOnlyList<RuleFinding> Findings,
    Localization.LocalizedText? NotApplicableReason = null);

public sealed record ScoreResult(int? Score, IReadOnlyList<AreaScore> Areas);

public static class ScoreCalculator
{
    private const decimal CriticalWeight = 15m;
    private const decimal WarningWeight = 5m;

    private const decimal RepeatFactor = 0.5m;

    private const int CapMultiplier = 3;

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

        int? score = scored.Count == 0 ? null : RoundHalfUp(scored.Sum() / (decimal)scored.Count);

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
