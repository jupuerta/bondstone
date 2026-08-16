namespace arch_forge_backend.Domain.Entities;

/// <summary>
/// V4 D5.4. What an architecture scored at one saved version - the debt V2 wrote as the
/// <c>61 → 78</c> narrative and V3 pushed forward twice.
///
/// <strong>Written when the architecture is saved, not when it is analysed.</strong> The analysis
/// fires on a debounce while you type, so recording it there would fill the table with the noise of
/// half-finished edits; <see cref="Version"/> already identifies the state of the graph without
/// ambiguity, and it only moves when something reached the database.
///
/// It is the one thing in V4 that persists a verdict, which is exactly why
/// <see cref="ScoringVersion"/> is stored next to the number: a score from <c>v2</c> and one from
/// <c>v4</c> are not comparable, and V4 is the version that proves it - eight new rules moved every
/// score, two of the four reference architectures <em>upwards</em>.
/// </summary>
public class ArchitectureSnapshot
{
    public Guid Id { get; set; }
    public Guid ArchitectureId { get; set; }

    /// <summary>The architecture's version this was measured over. Unique per architecture.</summary>
    public int Version { get; set; }

    public string ScoringVersion { get; set; } = string.Empty;

    /// <summary>Null when no area was scorable - an empty architecture is not a 100 (V2 D1.2).</summary>
    public int? Score { get; set; }

    /// <summary>The counts, as the JSON of <c>AnalysisSummary</c>. Enough to tell a story without storing every finding.</summary>
    public string Summary { get; set; } = "{}";

    public DateTime CreatedAt { get; set; }
}
