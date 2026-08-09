namespace arch_forge_backend.Application.Analysis;

public class AnalysisResult
{
    public int Score { get; init; }
    public List<RuleFinding> Findings { get; init; } = new();
}
