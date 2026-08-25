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
        // D1.2, rule 4: an empty architecture is a dash, never a 100.
        var result = new ArchitectureAnalyzer([]).Analyze(EmptyGraph);

        Assert.Null(result.Score);
        Assert.Empty(result.Findings);
        Assert.Equal(6, result.Areas.Count);
        Assert.All(result.Areas, area => Assert.Equal(AreaState.NoRules, area.State));
    }

    [Fact]
    public void Analyze_WhereNoRuleApplies_ReturnsNullScoreAndNoScoredArea()
    {
        // The same conclusion by the other road: rules exist, none of them had anything to check
        // here. Still not a 100 (D1.2, rules 3 and 4).
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
        // D1.5: which model produced the number, so a stored result can still be read later.
        Assert.Equal("v8", new ArchitectureAnalyzer([]).Analyze(EmptyGraph).ScoringVersion);
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
        // The cap of D1.1 is per rule code, not per area: 100 - 45 - 15 = 40. Were it per area,
        // Resilience could never drop past 55 no matter how many different things were wrong.
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
        // D5.2: the consumer must see the facts whatever order the rules were registered in -
        // here the consumer is registered first on purpose.
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
        // This is the assertion the whole two-phase design rests on (D5.2): if the order inside a
        // phase mattered, two phases would not be enough and the engine would need the DAG.
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
        // Facts from a previous run must not pile up: a producer publishing on every pass would
        // make the second analysis of an unchanged architecture disagree with the first.
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

    [Fact]
    public void Analyze_FindingsTiedOnSeverityAreaAndCode_AreOrderedByTheGraph()
    {
        // V6 D1.1, the fourth tie-break. Until V6 the three keys of D2.1 left the findings of one
        // rule tied, and the tie was broken by the order the rule emitted them in - which is the
        // order the graph arrived in, which the database does not guarantee. Now the element the
        // finding is about decides, so this rule reporting backwards still reads forwards.
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
        // The tie-break of V6 D1.1 needs an element to sort by, and a finding without one sorts
        // last. Only the Good of each rule has no elements and there is exactly one per code, so
        // in the product nothing ever ties here - but the order still has to be stable rather than
        // whatever OrderBy felt like.
        var result = new ArchitectureAnalyzer([
            StubRule.WithMessages(Area.Availability, RuleCodes.Spof, Severity.Critical, "first", "second", "third")
        ]).Analyze(EmptyGraph);

        Assert.Equal(["first", "second", "third"], result.Findings.Select(f => f.Message.Key));
    }

    [Fact]
    public void Constructor_TwoRulesSharingACode_Throws()
    {
        // The cap is per code (D1.1) and a code means one thing (D2.4): two rules answering to
        // the same one would double-penalize in silence.
        var exception = Assert.Throws<InvalidOperationException>(() => new ArchitectureAnalyzer([
            StubRule.WithFindings(Area.Availability, RuleCodes.Spof, Severity.Critical),
            StubRule.WithFindings(Area.Resilience, RuleCodes.Spof, Severity.Warning)
        ]));

        Assert.Contains(RuleCodes.Spof, exception.Message);
    }

    [Fact]
    public void Analyze_RuleReportingFindingsWhileNotApplicable_Throws()
    {
        // Not applicable means there was nothing to check (D1.4); findings under an area marked
        // as having nothing to say is a contradiction, not a state to render.
        var analyzer = new ArchitectureAnalyzer([new MisbehavingRule(applicable: false, matchingCode: true)]);

        Assert.Throws<InvalidOperationException>(() => analyzer.Analyze(EmptyGraph));
    }

    [Fact]
    public void Analyze_RuleEmittingAFindingUnderAnotherCode_Throws()
    {
        // Penalties are grouped by the rule's code and area; a finding that disagrees would be
        // charged to one heading and displayed under another.
        var analyzer = new ArchitectureAnalyzer([new MisbehavingRule(applicable: true, matchingCode: false)]);

        Assert.Throws<InvalidOperationException>(() => analyzer.Analyze(EmptyGraph));
    }

    private static AreaScore AreaOf(AnalysisResult result, Area area) => result.Areas.Single(a => a.Area == area);

    // Records compare their list members by reference, so the whole result is rendered to text to
    // compare two runs in one assertion - and a failure prints what actually differs.
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

    /// <summary>
    /// Reports one finding per node, walking the graph backwards. Emission order is the opposite of
    /// the order the analyzer has to produce, which is the only way to tell a real tie-break from
    /// the stability of OrderBy.
    /// </summary>
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
                return RuleResult.NotApplicable;

            return RuleResult.Applicable(_findings
                .Select(f => new RuleFinding(Code, Area, f.Severity, LocalizedText.Of(f.Message), LocalizedText.Of("explanation"), LocalizedText.Of("suggestion"), []))
                .ToList());
        }

        // Real codes, so the ordering tests use the same alphabet the product does.
        private static string CodeFor(Area area) => area switch
        {
            Area.Availability => RuleCodes.Spof,
            Area.Resilience => RuleCodes.SyncChain,
            Area.Coupling => RuleCodes.HighCoupling,
            _ => RuleCodes.MissingCache
        };
    }

    /// <summary>A producer: publishes facts and reports one warning for each.</summary>
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
                // Deterministic ids: two runs must produce byte-identical results.
                var nodeId = new Guid($"00000000-0000-0000-0000-{i:000000000000}");
                graph.Facts.Add(new SinglePointOfFailureFact(nodeId, Severity.Critical));
                findings.Add(new RuleFinding(code, area, Severity.Warning, LocalizedText.Of($"published {i}"), LocalizedText.Of("explanation"), null,
                    [FindingElement.Node(nodeId)]));
            }

            return RuleResult.Applicable(findings);
        }
    }

    /// <summary>A consumer: reports one warning per fact it can see, so its output changes if the phases ever leak.</summary>
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
