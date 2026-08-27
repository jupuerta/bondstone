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

        var duplicate = _rules.GroupBy(r => r.Code).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Rule code '{duplicate.Key}' is registered more than once.");
    }

    public AnalysisResult Analyze(ArchitectureGraph graph)
    {
        graph.Facts.Clear();

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

        var load = LoadModel.Of(graph);

        return new AnalysisResult(
            AnalysisResult.CurrentScoringVersion,
            score.Score,
            score.Areas,
            summary,
            Order(findings, Positions(graph)),
            Load(graph, load),
            Coverage(outcomes),
            CallLoad(graph, load),
            Cost(graph));
    }

    private static ArchitectureCost Cost(ArchitectureGraph graph)
    {
        var components = graph.Nodes
            .Select(node => new ComponentCost(
                node.Id,
                CostModel.PerInstance(node.Type),
                NodeSettings.Instances(node),
                NodeRoles.IsYoursToPayFor(node.Type)
                    ? CostModel.PerInstance(node.Type) * NodeSettings.Instances(node)
                    : 0))
            .ToList();

        return new ArchitectureCost(components.Sum(component => component.Total), components);
    }

    private static IReadOnlyList<ComponentLoad> Load(ArchitectureGraph graph, LoadModel load)
    {
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

    private static IReadOnlyList<ConnectionLoad> CallLoad(ArchitectureGraph graph, LoadModel load)
    {
        if (load.IsIdle)
            return [];

        return graph.Connections
            .Select(edge => new ConnectionLoad(edge.Id, Math.Round(load.CallLoadOf(edge.Id), 2)))
            .ToList();
    }

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

        if (!result.IsApplicable && result.Findings.Count > 0)
            throw new InvalidOperationException(
                $"Rule '{rule.Code}' reported findings while declaring itself not applicable.");

        if (!result.IsApplicable && result.NotApplicableReason is null)
            throw new InvalidOperationException(
                $"Rule '{rule.Code}' declared itself not applicable without saying why.");

        foreach (var finding in result.Findings)
        {
            if (finding.Code != rule.Code || finding.Area != rule.Area)
                throw new InvalidOperationException(
                    $"Rule '{rule.Code}' ({rule.Area}) emitted a finding for '{finding.Code}' ({finding.Area}).");
        }

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

    private static IReadOnlyList<RuleFinding> Order(
        IEnumerable<RuleFinding> findings,
        IReadOnlyDictionary<Guid, int> positions) => findings
        .OrderByDescending(f => f.Severity)
        .ThenBy(f => f.Area)
        .ThenBy(f => f.Code, StringComparer.Ordinal)
        .ThenBy(f => PositionOf(f, positions))
        .ToList();

    private static IReadOnlyDictionary<Guid, int> Positions(ArchitectureGraph graph)
    {
        var positions = new Dictionary<Guid, int>(graph.Nodes.Count + graph.Connections.Count);

        foreach (var node in graph.Nodes)
            positions[node.Id] = positions.Count;

        foreach (var connection in graph.Connections)
            positions[connection.Id] = positions.Count;

        return positions;
    }

    private static int PositionOf(RuleFinding finding, IReadOnlyDictionary<Guid, int> positions) =>
        finding.Elements.Count > 0 && positions.TryGetValue(finding.Elements[0].Id, out var position)
            ? position
            : int.MaxValue;
}
