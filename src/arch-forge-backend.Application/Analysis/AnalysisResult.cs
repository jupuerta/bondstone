namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// V2 D2.1, a clean redesign with no back-compatibility: front and back deploy together.
/// </summary>
/// <param name="ScoringVersion">Which scoring model produced this result (V2 D1.5).</param>
/// <param name="Score">Null when no area is scorable - an empty architecture is not a 100.</param>
public sealed record AnalysisResult(
    string ScoringVersion,
    int? Score,
    IReadOnlyList<AreaScore> Areas,
    AnalysisSummary Summary,
    IReadOnlyList<RuleFinding> Findings)
{
    /// <summary>
    /// V4 D1.4. Bumped because the eight rules of D5.2 make Security and Observability score for
    /// the first time, so <strong>every score of every architecture moves</strong> - and moves in
    /// both directions, because an average over six areas dilutes a bad one. Two figures produced
    /// by different scoring versions are not comparable, which is the whole reason this field
    /// travels (V2 D1.5) and why a challenge may never assert on one (D1.4).
    /// </summary>
    public const string CurrentScoringVersion = "v4";
}
