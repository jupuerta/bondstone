using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Analysis.Scoring;

namespace arch_forge_backend.Application.Analysis;

public class ArchitectureAnalyzer
{
    private readonly IReadOnlyList<IArchitectureRule> _rules;

    public ArchitectureAnalyzer(IEnumerable<IArchitectureRule> rules)
    {
        _rules = rules.ToList();
    }

    public AnalysisResult Analyze(ArchitectureGraph graph)
    {
        // Two fixed phases (D5.2): producers publish facts, consumers read them. Order inside a
        // phase is not guaranteed and must not matter - no rule reads facts from its own phase.
        var outcomes = new List<RuleOutcome>();
        foreach (var phase in (RulePhase[])[RulePhase.Producer, RulePhase.Consumer])
        {
            foreach (var rule in _rules.Where(r => r.Phase == phase))
                outcomes.Add(Evaluate(rule, graph));
        }

        var findings = outcomes.SelectMany(o => o.Findings).ToList();
        var score = ScoreCalculator.Calculate(outcomes);

        var summary = new AnalysisSummary(
            Critical: findings.Count(f => f.Severity == Severity.Critical),
            Warning: findings.Count(f => f.Severity == Severity.Warning),
            Passed: findings.Count(f => f.Severity == Severity.Good));

        return new AnalysisResult(
            AnalysisResult.CurrentScoringVersion,
            score.Score,
            score.Areas,
            summary,
            Order(findings));
    }

    private static RuleOutcome Evaluate(IArchitectureRule rule, ArchitectureGraph graph)
    {
        var result = rule.Evaluate(graph);

        // The Good finding is synthesized here, not by the rule: leaving it to each rule is
        // something one of them eventually forgets, and then the Passed counter lies in silence
        // (D5.2).
        var findings = result is { IsApplicable: true, Findings.Count: 0 }
            ? [Passed(rule)]
            : result.Findings;

        return new RuleOutcome(rule.Area, rule.Code, result.IsApplicable, findings);
    }

    private static RuleFinding Passed(IArchitectureRule rule) => new(
        rule.Code,
        rule.Area,
        Severity.Good,
        rule.PassedMessage,
        rule.PassedExplanation,
        Suggestion: null,
        Elements: []);

    // D2.1, rule 4. Without a deterministic order the reference-architecture tests would be
    // unstable. OrderBy is stable, so findings a rule emitted together keep their relative order.
    private static IReadOnlyList<RuleFinding> Order(IEnumerable<RuleFinding> findings) => findings
        .OrderByDescending(f => f.Severity)
        .ThenBy(f => f.Area)
        .ThenBy(f => f.Code, StringComparer.Ordinal)
        .ToList();
}
