using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Domain.Entities;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis;

public class ArchitectureAnalyzerTests
{
    private static readonly ArchitectureGraph EmptyGraph = new([], []);

    [Fact]
    public void Analyze_WithNoRules_ReturnsNullScoreAndNoScoredArea()
    {
        // D1.2, rule 4: an empty architecture is a dash, never a 100.
        var result = new ArchitectureAnalyzer([]).Analyze(EmptyGraph);

        Assert.Null(result.Score);
        Assert.Empty(result.Findings);
        Assert.Equal(6, result.Areas.Count);
        Assert.All(result.Areas, area => Assert.Equal(AreaState.NoRules, area.State));
    }

    [Fact]
    public void Analyze_CarriesTheScoringVersion()
    {
        // D1.5: which model produced the number, so a stored result can still be read later.
        Assert.Equal("v2", new ArchitectureAnalyzer([]).Analyze(EmptyGraph).ScoringVersion);
    }

    [Fact]
    public void Analyze_ApplicableRuleWithNoFindings_SynthesizesAGoodFinding()
    {
        // D5.2: the analyzer builds it, not the rule - otherwise a rule eventually forgets and
        // the Passed counter lies in silence.
        var rule = StubRule.Applicable(Area.Coupling);

        var result = new ArchitectureAnalyzer([rule]).Analyze(EmptyGraph);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(Severity.Good, finding.Severity);
        Assert.Equal(rule.PassedMessage, finding.Message);
        Assert.Equal(rule.PassedExplanation, finding.Explanation);
        Assert.Null(finding.Suggestion);
        Assert.Empty(finding.Elements);
        Assert.Equal(1, result.Summary.Passed);
    }

    [Fact]
    public void Analyze_ApplicableRuleWithNoFindings_LeavesItsAreaAtOneHundred()
    {
        var result = new ArchitectureAnalyzer([StubRule.Applicable(Area.Coupling)]).Analyze(EmptyGraph);

        Assert.Equal(new AreaScore(Area.Coupling, 100, AreaState.Scored), AreaOf(result, Area.Coupling));
        Assert.Equal(100, result.Score);
    }

    [Fact]
    public void Analyze_RuleThatDoesNotApply_LeavesItsAreaOutOfTheAverage()
    {
        // D1.2, rule 3: an area whose rules had nothing to check does not enter the average, and
        // does not count as a passed check either.
        var result = new ArchitectureAnalyzer([
            StubRule.NotApplicable(Area.Availability),
            StubRule.Applicable(Area.Coupling)
        ]).Analyze(EmptyGraph);

        Assert.Equal(AreaState.NotApplicable, AreaOf(result, Area.Availability).State);
        Assert.Null(AreaOf(result, Area.Availability).Score);
        Assert.Equal(100, result.Score);
        Assert.Equal(1, result.Summary.Passed);
    }

    [Fact]
    public void Analyze_ScoresEachAreaSeparatelyAndAveragesTheScoredOnes()
    {
        // Availability: one critical -> 85. Coupling: two warnings -> 100 - 5 - 2.5 = 92.5 -> 93.
        // Average of the two areas the user sees: (85 + 93) / 2 = 89.
        var result = new ArchitectureAnalyzer([
            StubRule.WithFindings(Area.Availability, "A_RULE", Severity.Critical),
            StubRule.WithFindings(Area.Coupling, "C_RULE", Severity.Warning, Severity.Warning)
        ]).Analyze(EmptyGraph);

        Assert.Equal(85, AreaOf(result, Area.Availability).Score);
        Assert.Equal(93, AreaOf(result, Area.Coupling).Score);
        Assert.Equal(89, result.Score);
        Assert.Equal(new AnalysisSummary(Critical: 1, Warning: 2, Passed: 0), result.Summary);
    }

    [Fact]
    public void Analyze_RunsEveryProducerBeforeAnyConsumer()
    {
        // D5.2: the consumer must see the facts, whatever order the rules were registered in.
        var seenByConsumer = new List<ArchitectureFact>();
        var producer = new FactProducingRule();
        var consumer = new FactReadingRule(seenByConsumer);

        new ArchitectureAnalyzer([consumer, producer]).Analyze(new ArchitectureGraph([], []));

        Assert.Single(seenByConsumer);
    }

    [Fact]
    public void Analyze_OrdersFindingsBySeverityThenAreaThenCode()
    {
        // D2.1, rule 4. Without this the reference-architecture tests would be unstable.
        var result = new ArchitectureAnalyzer([
            StubRule.Applicable(Area.Scalability),
            StubRule.WithFindings(Area.Resilience, "Z_RULE", Severity.Warning),
            StubRule.WithFindings(Area.Resilience, "A_RULE", Severity.Warning),
            StubRule.WithFindings(Area.Coupling, "M_RULE", Severity.Warning),
            StubRule.WithFindings(Area.Availability, "S_RULE", Severity.Critical)
        ]).Analyze(EmptyGraph);

        Assert.Equal(
            ["S_RULE", "A_RULE", "Z_RULE", "M_RULE", RuleCodes.MissingCache],
            result.Findings.Select(f => f.Code));
    }

    private static AreaScore AreaOf(AnalysisResult result, Area area) => result.Areas.Single(a => a.Area == area);

    private sealed class StubRule : IArchitectureRule
    {
        private readonly bool _isApplicable;
        private readonly Severity[] _severities;

        private StubRule(Area area, string code, bool isApplicable, Severity[] severities)
        {
            Area = area;
            Code = code;
            _isApplicable = isApplicable;
            _severities = severities;
        }

        public static StubRule Applicable(Area area) => new(area, CodeFor(area), true, []);

        public static StubRule NotApplicable(Area area) => new(area, CodeFor(area), false, []);

        public static StubRule WithFindings(Area area, string code, params Severity[] severities) =>
            new(area, code, true, severities);

        public string Code { get; }
        public Area Area { get; }
        public Severity DefaultSeverity => Severity.Warning;
        public RulePhase Phase => RulePhase.Consumer;
        public string PassedMessage => $"{Code} passed.";
        public string PassedExplanation => $"{Code} was checked.";

        public RuleResult Evaluate(ArchitectureGraph graph)
        {
            if (!_isApplicable)
                return RuleResult.NotApplicable;

            return RuleResult.Applicable(_severities
                .Select(severity => new RuleFinding(Code, Area, severity, "message", "explanation", "suggestion", []))
                .ToList());
        }

        // Real codes, so the ordering test uses the same alphabet the product does.
        private static string CodeFor(Area area) => area switch
        {
            Area.Availability => RuleCodes.Spof,
            Area.Resilience => RuleCodes.SyncChain,
            Area.Coupling => RuleCodes.HighCoupling,
            _ => RuleCodes.MissingCache
        };
    }

    private sealed class FactProducingRule : IArchitectureRule
    {
        public string Code => "PRODUCER";
        public Area Area => Area.Availability;
        public Severity DefaultSeverity => Severity.Warning;
        public RulePhase Phase => RulePhase.Producer;
        public string PassedMessage => "produced";
        public string PassedExplanation => "produced";

        public RuleResult Evaluate(ArchitectureGraph graph)
        {
            graph.Facts.Add(new SinglePointOfFailureFact(Guid.NewGuid(), Severity.Critical));
            return RuleResult.Applicable();
        }
    }

    private sealed class FactReadingRule(List<ArchitectureFact> seen) : IArchitectureRule
    {
        public string Code => "CONSUMER";
        public Area Area => Area.Resilience;
        public Severity DefaultSeverity => Severity.Warning;
        public RulePhase Phase => RulePhase.Consumer;
        public string PassedMessage => "consumed";
        public string PassedExplanation => "consumed";

        public RuleResult Evaluate(ArchitectureGraph graph)
        {
            seen.AddRange(graph.Facts);
            return RuleResult.Applicable();
        }
    }
}
