using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Analysis.Scoring;

namespace arch_forge_backend.Application.Analysis;

public class ArchitectureAnalyzer
{
    private readonly IReadOnlyList<IArchitectureRule> _rules;

    public ArchitectureAnalyzer(IEnumerable<IArchitectureRule> rules)
    {
        _rules = rules.ToList();

        // Penalties are capped per rule code (D1.1) and a code never means two things (D2.4), so
        // two rules answering to the same code would silently double-penalize an area and make
        // the score impossible to reconstruct by hand. It is a registration mistake, and the
        // moment to notice it is startup, not the first analysis that looks slightly off.
        var duplicate = _rules.GroupBy(r => r.Code).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Rule code '{duplicate.Key}' is registered more than once.");
    }

    public AnalysisResult Analyze(ArchitectureGraph graph)
    {
        // Analyzing the same graph twice has to give the same answer, so the facts of a previous
        // run are not left lying around for the producers to add to.
        graph.Facts.Clear();

        // Two fixed phases (D5.2): every producer runs before every consumer. Order inside a
        // phase is not guaranteed and must not matter - no rule reads facts published by a rule
        // of its own phase, and a test runs each phase backwards to keep it that way.
        var outcomes = new List<RuleOutcome>();
        foreach (var phase in Enum.GetValues<RulePhase>())
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

        // "Not applicable" means there was nothing to check, and D1.4 says that contributes
        // nothing: no findings, no effect on the area. A rule reporting both would produce
        // findings the user reads under an area marked as having nothing to say.
        if (!result.IsApplicable && result.Findings.Count > 0)
            throw new InvalidOperationException(
                $"Rule '{rule.Code}' reported findings while declaring itself not applicable.");

        foreach (var finding in result.Findings)
        {
            // The score groups penalties by the rule's code and area; a finding that disagrees
            // would be penalized under one heading and displayed under another.
            if (finding.Code != rule.Code || finding.Area != rule.Area)
                throw new InvalidOperationException(
                    $"Rule '{rule.Code}' ({rule.Area}) emitted a finding for '{finding.Code}' ({finding.Area}).");
        }

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
    // unstable. OrderBy is stable, so findings a rule emitted together keep their relative order,
    // which is the last tie-break the contract asks for.
    private static IReadOnlyList<RuleFinding> Order(IEnumerable<RuleFinding> findings) => findings
        .OrderByDescending(f => f.Severity)
        .ThenBy(f => f.Area)
        .ThenBy(f => f.Code, StringComparer.Ordinal)
        .ToList();
}
