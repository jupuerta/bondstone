namespace bondstone_backend.Application.Analysis;

public sealed record AreaScore(Area Area, int? Score, AreaState State, IReadOnlyList<PenaltyLine>? Penalties = null)
{
    public IReadOnlyList<PenaltyLine> Penalties { get; init; } = Penalties ?? [];

    public bool Equals(AreaScore? other) =>
        other is not null
        && Area == other.Area
        && Score == other.Score
        && State == other.State
        && Penalties.SequenceEqual(other.Penalties);

    public override int GetHashCode() => HashCode.Combine(Area, Score, State, Penalties.Count);
}

public sealed record PenaltyLine(string Code, Severity Severity, int Findings, decimal Penalty, bool Capped);
