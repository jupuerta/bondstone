namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// <see cref="Score"/> is null unless <see cref="State"/> is <see cref="AreaState.Scored"/>: an
/// unevaluated area shows a dash, never 100 (V2 D1.2).
/// </summary>
public sealed record AreaScore(Area Area, int? Score, AreaState State);
