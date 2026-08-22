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
    /// V13 D1.4. Bumped because SPOF stopped counting arrows and started reading them (D1.1): an
    /// edge that is asynchronous, or that declares the fallback its caller takes instead, no longer
    /// counts as a component falling with the one it points at. <strong>Availability moves on every
    /// architecture that had ever configured a fallback</strong> - upwards, and most on the ones
    /// that had done the most work, which is the whole point.
    ///
    /// The reference suite measures the size of it: <c>resilient-async</c> goes from 92 to 93 and
    /// its Availability from 90 to 95, losing two of its three SPOFs and <strong>keeping the
    /// third</strong> - the database, because a fallback in front of the system of record buys a
    /// stale read and never a write (D1.1). The other three fixtures do not move a point: there is
    /// not one asynchronous edge nor one declared fallback between them, so there was nothing to
    /// re-read. A bump that only moves the architecture that had done the work is the shape this
    /// change was supposed to have.
    ///
    /// V4 bumped this for the opposite reason - two unmeasured areas starting to be measured, which
    /// moved every score in both directions. Two figures produced by different scoring versions are
    /// not comparable either way, which is the whole reason this field travels (V2 D1.5) and why a
    /// challenge may never assert on one (V4 D1.4).
    /// </summary>
    public const string CurrentScoringVersion = "v5";
}
