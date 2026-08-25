using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Analysis.Scoring;
using arch_forge_backend.Application.Simulation;

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
            Order(findings, Positions(graph)),
            Load(graph),
            Coverage(outcomes));
    }

    /// <summary>
    /// V16 C5. The load axis, published instead of consumed. <see cref="LoadModel"/> is computed
    /// here for the same reason five rules already compute it - it is a pure function of the graph
    /// and nothing carries it between them - and the one thing that is new is that the numbers
    /// leave the analyzer instead of ending inside <c>UNDERSIZED_COMPONENT</c>'s verdict.
    ///
    /// Nothing at all when nobody declared traffic: see <see cref="AnalysisResult.Load"/>. One
    /// entry per component, in the canonical order of <see cref="ArchitectureGraph.Nodes"/> - which
    /// is contract (V6 D1.1), and the order the export already lists the components in.
    /// </summary>
    private static IReadOnlyList<ComponentLoad> Load(ArchitectureGraph graph)
    {
        var load = LoadModel.Of(graph);

        if (load.IsIdle)
            return [];

        return graph.Nodes
            .Select(node => new ComponentLoad(
                node.Id,
                Math.Round(load.LoadOf(node.Id), 2),
                load.CapacityOf(node.Id),
                Math.Round(load.UtilizationOf(node.Id), 2)))
            .ToList();
    }

    /// <summary>
    /// V16 C7a. What every registered rule did here, including the ones that had nothing to look
    /// at - which is the state the user never got to see, and the one that says what their drawing
    /// is missing.
    ///
    /// The three states are read off the outcome the analyzer already built: not applicable is
    /// <see cref="RuleResult.IsApplicable"/>, and among those that did apply the split is the same
    /// one <see cref="AnalysisSummary.Passed"/> counts - the synthesized <see cref="Severity.Good"/>
    /// finding of <see cref="Evaluate"/> means the rule ran and found nothing.
    ///
    /// Ordered by area and then by code so the catalogue reads the same however the rules were
    /// registered, which is the same reason the findings are ordered (D2.1, rule 4).
    ///
    /// V16 C7b. The not-applicable ones carry the sentence their rule stood down with. The other
    /// two states carry none: a rule that ran has its findings to speak with, and a "why not" on
    /// something that did apply would be a sentence about a question nobody asked.
    /// </summary>
    private static IReadOnlyList<RuleCoverage> Coverage(IEnumerable<RuleOutcome> outcomes) => outcomes
        .Select(outcome => new RuleCoverage(
            outcome.Code,
            outcome.Area,
            !outcome.IsApplicable ? RuleState.NotApplicable
                : outcome.Findings.All(f => f.Severity == Severity.Good) ? RuleState.Passed
                : RuleState.Fired,
            outcome.NotApplicableReason))
        .OrderBy(rule => rule.Area)
        .ThenBy(rule => rule.Code, StringComparer.Ordinal)
        .ToList();

    private static RuleOutcome Evaluate(IArchitectureRule rule, ArchitectureGraph graph)
    {
        var result = rule.Evaluate(graph);

        // "Not applicable" means there was nothing to check, and D1.4 says that contributes
        // nothing: no findings, no effect on the area. A rule reporting both would produce
        // findings the user reads under an area marked as having nothing to say.
        if (!result.IsApplicable && result.Findings.Count > 0)
            throw new InvalidOperationException(
                $"Rule '{rule.Code}' reported findings while declaring itself not applicable.");

        // V16 C7b. A rule that stands down owes the user a reason, and the catalogue of C7 paints
        // it on the card. Checked here rather than trusted to <see cref="RuleResult.NotApplicable"/>
        // because a rule can also build the record directly, and a silent not-applicable is a blank
        // line on a screen instead of the sentence that says what the drawing is missing.
        if (!result.IsApplicable && result.NotApplicableReason is null)
            throw new InvalidOperationException(
                $"Rule '{rule.Code}' declared itself not applicable without saying why.");

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

        return new RuleOutcome(rule.Area, rule.Code, result.IsApplicable, findings, result.NotApplicableReason);
    }

    private static RuleFinding Passed(IArchitectureRule rule) => new(
        rule.Code,
        rule.Area,
        Severity.Good,
        rule.PassedMessage,
        rule.PassedExplanation,
        Suggestion: null,
        Elements: []);

    // D2.1, rule 4, with the fourth tie-break V6 D1.1 adds. The first three - severity, area,
    // code - leave the findings of one rule tied with each other, and until V6 that tie was
    // resolved by the stability of OrderBy, which means by the order the rule emitted them in,
    // which means by the order the graph arrived in. Now it is resolved by where the element the
    // finding talks about sits in the canonical order of the graph: the three SPOFs of
    // resilient-async read Db, Queue, Cache - by type first, which is the order the export lists
    // the components in, not alphabetical by name.
    private static IReadOnlyList<RuleFinding> Order(
        IEnumerable<RuleFinding> findings,
        IReadOnlyDictionary<Guid, int> positions) => findings
        .OrderByDescending(f => f.Severity)
        .ThenBy(f => f.Area)
        .ThenBy(f => f.Code, StringComparer.Ordinal)
        .ThenBy(f => PositionOf(f, positions))
        .ToList();

    /// <summary>
    /// Where each element sits in the graph: the nodes in their canonical order, then the edges in
    /// theirs. One numbering for both, because a finding points at whichever it is about.
    /// </summary>
    private static IReadOnlyDictionary<Guid, int> Positions(ArchitectureGraph graph)
    {
        var positions = new Dictionary<Guid, int>(graph.Nodes.Count + graph.Connections.Count);

        foreach (var node in graph.Nodes)
            positions[node.Id] = positions.Count;

        foreach (var connection in graph.Connections)
            positions[connection.Id] = positions.Count;

        return positions;
    }

    /// <summary>
    /// The first element is the one the finding is about - the coupled node, the edge without a
    /// timeout - and the rest is context it drags along, so it is the first that orders. A finding
    /// with no elements sorts last: the only one is the Good of each rule, and there is exactly one
    /// per code, so it never ties with anything.
    /// </summary>
    private static int PositionOf(RuleFinding finding, IReadOnlyDictionary<Guid, int> positions) =>
        finding.Elements.Count > 0 && positions.TryGetValue(finding.Elements[0].Id, out var position)
            ? position
            : int.MaxValue;
}
