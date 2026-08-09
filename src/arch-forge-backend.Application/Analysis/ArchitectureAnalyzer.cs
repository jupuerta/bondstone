using arch_forge_backend.Application.Analysis.Rules;

namespace arch_forge_backend.Application.Analysis;

public class ArchitectureAnalyzer
{
    private readonly IEnumerable<IArchitectureRule> _rules;

    public ArchitectureAnalyzer(IEnumerable<IArchitectureRule> rules)
    {
        _rules = rules;
    }

    public AnalysisResult Analyze(ArchitectureGraph graph)
    {
        var findings = _rules.SelectMany(rule => rule.Evaluate(graph)).ToList();

        var criticalCount = findings.Count(f => f.Severity == Severity.Critical);
        var warningCount = findings.Count(f => f.Severity == Severity.Warning);
        var score = Math.Max(0, 100 - (criticalCount * 15) - (warningCount * 5));

        return new AnalysisResult { Score = score, Findings = findings };
    }
}
