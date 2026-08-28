using arch_forge_backend.Application.Tests.TestDoubles;
using System.Text;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis;

public class ArchitectureAnalyzerTests
{
    private static readonly ArchitectureGraph EmptyGraph = new([], []);

    [Fact]
    public void Analyze_WithNoRules_ReturnsNullScoreAndNoScoredArea()
    {
        var result = new ArchitectureAnalyzer([]).Analyze(EmptyGraph);

        Assert.Null(result.Score);
        Assert.Empty(result.Findings);
        Assert.Equal(7, result.Areas.Count);
        Assert.All(result.Areas, area => Assert.Equal(AreaState.NoRules, area.State));
    }

    [Fact]
    public void Analyze_WhereNoRuleApplies_ReturnsNullScoreAndNoScoredArea()
    {
        var result = new ArchitectureAnalyzer([
            StubRule.NotApplicable(Area.Availability),
            StubRule.NotApplicable(Area.Resilience)
        ]).Analyze(EmptyGraph);

        Assert.Null(result.Score);
        Assert.Empty(result.Findings);
        Assert.DoesNotContain(result.Areas, area => area.State == AreaState.Scored);
        Assert.Equal(0, result.Summary.Passed);
    }

    [Fact]
    public void Analyze_CarriesTheScoringVersion()
    {
        Assert.Equal("v9", new ArchitectureAnalyzer([]).Analyze(EmptyGraph).ScoringVersion);
    }

    [Fact]
    public void Analyze_ApplicableRuleWithNoFindings_SynthesizesAGoodFinding()
    {
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
    public void Analyze_RuleWithFindings_DoesNotAlsoCountAsPassed()
    {
        var result = new ArchitectureAnalyzer([
            StubRule.WithFindings(Area.Availability, RuleCodes.Spof, Severity.Critical)
        ]).Analyze(EmptyGraph);

        Assert.Equal(0, result.Summary.Passed);
        Assert.DoesNotContain(result.Findings, f => f.Severity == Severity.Good);
    }

    [Fact]
    public void Analyze_RuleThatDoesNotApply_LeavesItsAreaOutOfTheAverage()
    {
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
        var result = new ArchitectureAnalyzer([
            StubRule.WithFindings(Area.Availability, RuleCodes.Spof, Severity.Critical),
            StubRule.WithFindings(Area.Coupling, RuleCodes.HighCoupling, Severity.Warning, Severity.Warning)
        ]).Analyze(EmptyGraph);

        Assert.Equal(85, AreaOf(result, Area.Availability).Score);
        Assert.Equal(93, AreaOf(result, Area.Coupling).Score);
        Assert.Equal(89, result.Score);
        Assert.Equal(new AnalysisSummary(Critical: 1, Warning: 2, Passed: 0), result.Summary);
    }

    [Fact]
    public void Analyze_AreaFedBySeveralRules_CapsEachRuleOnItsOwn()
    {
        var result = new ArchitectureAnalyzer([
            StubRule.WithFindings(Area.Resilience, RuleCodes.MissingResilience,
                Enumerable.Repeat(Severity.Critical, 6).ToArray()),
            StubRule.WithFindings(Area.Resilience, RuleCodes.SyncChain, Severity.Critical)
        ]).Analyze(EmptyGraph);

        Assert.Equal(40, AreaOf(result, Area.Resilience).Score);
    }

    [Fact]
    public void Analyze_RunsEveryProducerBeforeAnyConsumer()
    {
        var seenByConsumer = new List<ArchitectureFact>();

        new ArchitectureAnalyzer([
            new FactConsumingRule(Area.Resilience, RuleCodes.MissingResilience, seenByConsumer),
            new FactPublishingRule(Area.Availability, RuleCodes.Spof, factCount: 2)
        ]).Analyze(new ArchitectureGraph([], []));

        Assert.Equal(2, seenByConsumer.Count);
    }

    [Fact]
    public void Analyze_OrderOfRulesWithinAPhase_DoesNotChangeTheResult()
    {
        IArchitectureRule[] rules = [
            new FactPublishingRule(Area.Availability, RuleCodes.Spof, factCount: 2),
            new FactPublishingRule(Area.Coupling, RuleCodes.Cycle, factCount: 1),
            new FactConsumingRule(Area.Resilience, RuleCodes.MissingResilience, []),
            new FactConsumingRule(Area.Scalability, RuleCodes.MissingCache, [])
        ];

        var forwards = new ArchitectureAnalyzer(rules).Analyze(new ArchitectureGraph([], []));
        var backwards = new ArchitectureAnalyzer(rules.Reverse()).Analyze(new ArchitectureGraph([], []));

        Assert.Equal(Render(forwards), Render(backwards));
    }

    [Fact]
    public void Analyze_RunTwiceOverTheSameGraph_GivesTheSameResult()
    {
        var analyzer = new ArchitectureAnalyzer([
            new FactPublishingRule(Area.Availability, RuleCodes.Spof, factCount: 2),
            new FactConsumingRule(Area.Resilience, RuleCodes.MissingResilience, [])
        ]);
        var graph = new ArchitectureGraph([], []);

        Assert.Equal(Render(analyzer.Analyze(graph)), Render(analyzer.Analyze(graph)));
    }

    [Fact]
    public void Analyze_OrdersFindingsBySeverityThenAreaThenCode()
    {
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

    [Fact]
    public void Analyze_FindingsTiedOnSeverityAreaAndCode_AreOrderedByTheGraph()
    {
        var graph = new ArchitectureGraph(
            [Node("Zeta", NodeType.Service), Node("Db", NodeType.Database), Node("Api", NodeType.Api)],
            []);

        var result = new ArchitectureAnalyzer([
            new BackwardsNodeRule(Area.Availability, RuleCodes.Spof, Severity.Critical)
        ]).Analyze(graph);

        Assert.Equal(["Api", "Db", "Zeta"], result.Findings.Select(f => f.Message.Key));
    }

    [Fact]
    public void Analyze_SeveralFindingsPointingAtNothing_KeepTheOrderItEmittedThem()
    {
        var result = new ArchitectureAnalyzer([
            StubRule.WithMessages(Area.Availability, RuleCodes.Spof, Severity.Critical, "first", "second", "third")
        ]).Analyze(EmptyGraph);

        Assert.Equal(["first", "second", "third"], result.Findings.Select(f => f.Message.Key));
    }

    [Fact]
    public void Constructor_TwoRulesSharingACode_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new ArchitectureAnalyzer([
            StubRule.WithFindings(Area.Availability, RuleCodes.Spof, Severity.Critical),
            StubRule.WithFindings(Area.Resilience, RuleCodes.Spof, Severity.Warning)
        ]));

        Assert.Contains(RuleCodes.Spof, exception.Message);
    }

    [Fact]
    public void Analyze_RuleReportingFindingsWhileNotApplicable_Throws()
    {
        var analyzer = new ArchitectureAnalyzer([new MisbehavingRule(applicable: false, matchingCode: true)]);

        Assert.Throws<InvalidOperationException>(() => analyzer.Analyze(EmptyGraph));
    }

    [Fact]
    public void Analyze_RuleNotApplicableWithoutSayingWhy_Throws()
    {
        var analyzer = new ArchitectureAnalyzer([new SilentRule()]);

        var exception = Assert.Throws<InvalidOperationException>(() => analyzer.Analyze(EmptyGraph));

        Assert.Contains(RuleCodes.Spof, exception.Message);
    }

    [Fact]
    public void Analyze_RuleEmittingAFindingUnderAnotherCode_Throws()
    {
        var analyzer = new ArchitectureAnalyzer([new MisbehavingRule(applicable: true, matchingCode: false)]);

        Assert.Throws<InvalidOperationException>(() => analyzer.Analyze(EmptyGraph));
    }

    private static AreaScore AreaOf(AnalysisResult result, Area area) => result.Areas.Single(a => a.Area == area);

    private static string Render(AnalysisResult result)
    {
        var rendered = new StringBuilder()
            .AppendLine($"{result.ScoringVersion} score={result.Score?.ToString() ?? "-"}")
            .AppendLine($"summary={result.Summary}");

        foreach (var area in result.Areas)
            rendered.AppendLine($"area {area.Area} {area.State} {area.Score?.ToString() ?? "-"}");

        foreach (var finding in result.Findings)
        {
            var elements = string.Join(",", finding.Elements.Select(e => $"{e.Type}:{e.Id}"));
            rendered.AppendLine($"finding {finding.Severity} {finding.Area} {finding.Code} '{finding.Message.Key}' [{elements}]");
        }

        return rendered.ToString();
    }

    private static Node Node(string name, NodeType type) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Name = name,
        Configuration = "{}"
    };

    private sealed class BackwardsNodeRule(Area area, string code, Severity severity) : IArchitectureRule
    {
        public string Code => code;
        public Area Area => area;
        public Severity DefaultSeverity => severity;
        public RulePhase Phase => RulePhase.Consumer;
        public LocalizedText PassedMessage => LocalizedText.Of($"{code} passed.");
        public LocalizedText PassedExplanation => LocalizedText.Of($"{code} was checked.");

        public RuleResult Evaluate(ArchitectureGraph graph) => RuleResult.Applicable(graph.Nodes
            .Reverse()
            .Select(node => new RuleFinding(
                code, area, severity, LocalizedText.Of(node.Name), LocalizedText.Of("explanation"), LocalizedText.Of("suggestion"), [FindingElement.Node(node.Id)]))
            .ToList());
    }

    private sealed class StubRule : IArchitectureRule
    {
        private readonly bool _isApplicable;
        private readonly (Severity Severity, string Message)[] _findings;

        private StubRule(Area area, string code, bool isApplicable, (Severity, string)[] findings)
        {
            Area = area;
            Code = code;
            _isApplicable = isApplicable;
            _findings = findings;
        }

        public static StubRule Applicable(Area area) => new(area, CodeFor(area), true, []);

        public static StubRule NotApplicable(Area area) => new(area, CodeFor(area), false, []);

        public static StubRule WithFindings(Area area, string code, params Severity[] severities) =>
            new(area, code, true, severities.Select(s => (s, "message")).ToArray());

        public static StubRule WithMessages(Area area, string code, Severity severity, params string[] messages) =>
            new(area, code, true, messages.Select(m => (severity, m)).ToArray());

        public string Code { get; }
        public Area Area { get; }
        public Severity DefaultSeverity => Severity.Warning;
        public RulePhase Phase => RulePhase.Consumer;
        public LocalizedText PassedMessage => LocalizedText.Of($"{Code} passed.");
        public LocalizedText PassedExplanation => LocalizedText.Of($"{Code} was checked.");

        public RuleResult Evaluate(ArchitectureGraph graph)
        {
            if (!_isApplicable)
                return RuleResult.NotApplicable($"{Code} had nothing to look at.");

            return RuleResult.Applicable(_findings
                .Select(f => new RuleFinding(Code, Area, f.Severity, LocalizedText.Of(f.Message), LocalizedText.Of("explanation"), LocalizedText.Of("suggestion"), []))
                .ToList());
        }

        private static string CodeFor(Area area) => area switch
        {
            Area.Availability => RuleCodes.Spof,
            Area.Resilience => RuleCodes.SyncChain,
            Area.Coupling => RuleCodes.HighCoupling,
            _ => RuleCodes.MissingCache
        };
    }

    private sealed class FactPublishingRule(Area area, string code, int factCount) : IArchitectureRule
    {
        public string Code => code;
        public Area Area => area;
        public Severity DefaultSeverity => Severity.Warning;
        public RulePhase Phase => RulePhase.Producer;
        public LocalizedText PassedMessage => LocalizedText.Of($"{code} passed.");
        public LocalizedText PassedExplanation => LocalizedText.Of($"{code} was checked.");

        public RuleResult Evaluate(ArchitectureGraph graph)
        {
            var findings = new List<RuleFinding>();

            for (var i = 0; i < factCount; i++)
            {
                var nodeId = new Guid($"00000000-0000-0000-0000-{i:000000000000}");
                graph.Facts.Add(new SinglePointOfFailureFact(nodeId, Severity.Critical));
                findings.Add(new RuleFinding(code, area, Severity.Warning, LocalizedText.Of($"published {i}"), LocalizedText.Of("explanation"), null,
                    [FindingElement.Node(nodeId)]));
            }

            return RuleResult.Applicable(findings);
        }
    }

    private sealed class FactConsumingRule(Area area, string code, List<ArchitectureFact> seen) : IArchitectureRule
    {
        public string Code => code;
        public Area Area => area;
        public Severity DefaultSeverity => Severity.Warning;
        public RulePhase Phase => RulePhase.Consumer;
        public LocalizedText PassedMessage => LocalizedText.Of($"{code} passed.");
        public LocalizedText PassedExplanation => LocalizedText.Of($"{code} was checked.");

        public RuleResult Evaluate(ArchitectureGraph graph)
        {
            seen.AddRange(graph.Facts);

            return RuleResult.Applicable(graph.Facts
                .OfType<SinglePointOfFailureFact>()
                .OrderBy(fact => fact.NodeId)
                .Select(fact => new RuleFinding(code, area, Severity.Warning, LocalizedText.Of($"consumed {fact.NodeId}"), LocalizedText.Of("explanation"), null,
                    [FindingElement.Node(fact.NodeId)]))
                .ToList());
        }
    }

    private sealed class SilentRule : IArchitectureRule
    {
        public string Code => RuleCodes.Spof;
        public Area Area => Area.Availability;
        public Severity DefaultSeverity => Severity.Warning;
        public RulePhase Phase => RulePhase.Producer;
        public LocalizedText PassedMessage => LocalizedText.Of("passed");
        public LocalizedText PassedExplanation => LocalizedText.Of("checked");

        public RuleResult Evaluate(ArchitectureGraph graph) => new(false, []);
    }

    private sealed class MisbehavingRule(bool applicable, bool matchingCode) : IArchitectureRule
    {
        public string Code => RuleCodes.Spof;
        public Area Area => Area.Availability;
        public Severity DefaultSeverity => Severity.Warning;
        public RulePhase Phase => RulePhase.Producer;
        public LocalizedText PassedMessage => LocalizedText.Of($"passed");
        public LocalizedText PassedExplanation => LocalizedText.Of($"checked");

        public RuleResult Evaluate(ArchitectureGraph graph) => new(applicable, [
            new RuleFinding(
                matchingCode ? Code : RuleCodes.Cycle,
                Area,
                Severity.Warning,
                LocalizedText.Of("message"),
                LocalizedText.Of("explanation"),
                null,
                [])
        ]);
    }
}
