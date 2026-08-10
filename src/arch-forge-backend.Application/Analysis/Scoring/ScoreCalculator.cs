namespace arch_forge_backend.Application.Analysis.Scoring;

/// <summary>
/// What one rule had to say about one architecture: which area it feeds, whether it had anything
/// to check here (D1.4), and what it found.
/// </summary>
public sealed record RuleOutcome(Area Area, string Code, bool IsApplicable, IReadOnlyList<RuleFinding> Findings);

public sealed record ScoreResult(int? Score, IReadOnlyList<AreaScore> Areas);

/// <summary>
/// The arithmetic of V2 D1.1, kept apart from the analyzer's phase orchestration so a scoring test
/// never has to build rules.
///
/// <code>
/// Area   = max(0, 100 - Σ penalties of its rules)
/// Global = simple average of the Scored areas
/// </code>
///
/// Every area weighs the same regardless of how many rules feed it - that is the whole point of
/// grouping by area (D1.2).
/// </summary>
public static class ScoreCalculator
{
    private const decimal CriticalWeight = 15m;
    private const decimal WarningWeight = 5m;

    /// <summary>
    /// Diminishing returns: the first finding of a code costs full weight, the rest half.
    /// </summary>
    private const decimal RepeatFactor = 0.5m;

    /// <summary>
    /// Cap per rule code, as a multiple of its base weight. 3x and not 2x: with 2x an area fed by
    /// a single rule could not drop below 70, which turned whole areas into deaf indicators
    /// (D1.1, correction v1.1).
    /// </summary>
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
                // Nobody evaluates this area yet (Security, Observability in V2). It does not
                // score 100 and it does not enter the average: an unchecked area is not a healthy
                // area (D1.2, rule 2).
                areas.Add(new AreaScore(area, null, AreaState.NoRules));
                continue;
            }

            if (!areaOutcomes.Any(o => o.IsApplicable))
            {
                areas.Add(new AreaScore(area, null, AreaState.NotApplicable));
                continue;
            }

            var penalty = areaOutcomes
                .GroupBy(o => o.Code)
                .Sum(byCode => PenaltyForCode(byCode.SelectMany(o => o.Findings).ToList()));

            // Rounded here, before the average, so the four numbers the user sees are exactly the
            // ones that add up to the big one (D1.1).
            var areaScore = RoundHalfUp(Math.Max(0m, 100m - penalty));
            areas.Add(new AreaScore(area, areaScore, AreaState.Scored));
            scored.Add(areaScore);
        }

        // No scorable area at all - empty or trivial architecture. The UI shows a dash, never 100
        // (D1.2, rule 4).
        int? score = scored.Count == 0 ? null : RoundHalfUp(scored.Sum() / (decimal)scored.Count);

        return new ScoreResult(score, areas);
    }

    private static decimal PenaltyForCode(IReadOnlyList<RuleFinding> findings)
    {
        // Good findings weigh 0 (D1.1): they mark the area as checked, they do not move it.
        var penalized = findings
            .Where(f => f.Severity is Severity.Warning or Severity.Critical)
            // When one rule emits findings of different severity, the worst one takes the full
            // weight and the rest half (D1.1).
            .OrderByDescending(f => f.Severity)
            .ToList();

        if (penalized.Count == 0)
            return 0m;

        var total = 0m;
        for (var i = 0; i < penalized.Count; i++)
        {
            var weight = WeightOf(penalized[i].Severity);
            total += i == 0 ? weight : weight * RepeatFactor;
        }

        // The cap uses the highest severity present, which is the first after the sort.
        var cap = WeightOf(penalized[0].Severity) * CapMultiplier;
        return Math.Min(total, cap);
    }

    private static decimal WeightOf(Severity severity) => severity switch
    {
        Severity.Critical => CriticalWeight,
        Severity.Warning => WarningWeight,
        _ => 0m
    };

    private static int RoundHalfUp(decimal value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
