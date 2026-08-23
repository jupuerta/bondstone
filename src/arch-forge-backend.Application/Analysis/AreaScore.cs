namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// <see cref="Score"/> is null unless <see cref="State"/> is <see cref="AreaState.Scored"/>: an
/// unevaluated area shows a dash, never 100 (V2 D1.2).
///
/// <strong>QA §4, "explica esta nota".</strong> <see cref="Penalties"/> is the arithmetic of D1.1
/// written out - one line per rule code that cost the area something, with what it cost - so the
/// number can be redone by hand from the panel and not only from the source. It was always
/// reconstructible (the QA checked every scenario against <c>ScoreCalculator</c>); what changed is
/// who gets to see it. Empty on an unscored area and on a perfect one.
/// </summary>
public sealed record AreaScore(Area Area, int? Score, AreaState State, IReadOnlyList<PenaltyLine>? Penalties = null)
{
    public IReadOnlyList<PenaltyLine> Penalties { get; init; } = Penalties ?? [];

    // Value equality over the lines too: a test that says "this area reads 100, Scored" should
    // not fail because two empty lists are two objects.
    public bool Equals(AreaScore? other) =>
        other is not null
        && Area == other.Area
        && Score == other.Score
        && State == other.State
        && Penalties.SequenceEqual(other.Penalties);

    public override int GetHashCode() => HashCode.Combine(Area, Score, State, Penalties.Count);
}

/// <summary>
/// What one rule code cost one area. <see cref="Findings"/> is how many of its findings were
/// penalized, <see cref="Severity"/> the worst of them (the one that sets the cap), and
/// <see cref="Capped"/> whether the cap of D1.1 - three times the weight - is what the number is.
/// </summary>
public sealed record PenaltyLine(string Code, Severity Severity, int Findings, decimal Penalty, bool Capped);
