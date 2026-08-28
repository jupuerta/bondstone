namespace arch_forge_backend.Application.Analysis;

public sealed record AnalysisResult(
    string ScoringVersion,
    int? Score,
    IReadOnlyList<AreaScore> Areas,
    AnalysisSummary Summary,
    IReadOnlyList<RuleFinding> Findings,
    IReadOnlyList<ComponentLoad>? Load = null,
    IReadOnlyList<RuleCoverage>? Coverage = null,
    IReadOnlyList<ConnectionLoad>? CallLoad = null,
    ArchitectureCost? Cost = null)
{
    public IReadOnlyList<ComponentLoad> Load { get; init; } = Load ?? [];

    public IReadOnlyList<ConnectionLoad> CallLoad { get; init; } = CallLoad ?? [];

    public IReadOnlyList<RuleCoverage> Coverage { get; init; } = Coverage ?? [];

    public ArchitectureCost Cost { get; init; } = Cost ?? new ArchitectureCost(0, []);

    public const string CurrentScoringVersion = "v10";
}
