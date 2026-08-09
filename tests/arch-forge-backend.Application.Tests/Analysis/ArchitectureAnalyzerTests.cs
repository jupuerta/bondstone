using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Domain.Entities;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis;

public class ArchitectureAnalyzerTests
{
    private static readonly ArchitectureGraph EmptyGraph = new(Array.Empty<Node>(), Array.Empty<Connection>());

    [Fact]
    public void Analyze_WithNoFindings_ReturnsScoreOf100()
    {
        var analyzer = new ArchitectureAnalyzer(Array.Empty<IArchitectureRule>());

        var result = analyzer.Analyze(EmptyGraph);

        Assert.Equal(100, result.Score);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData(1, 0, 85)]
    [InlineData(0, 1, 95)]
    [InlineData(2, 3, 55)]
    public void Analyze_DeductsPerFindingSeverity(int criticalCount, int warningCount, int expectedScore)
    {
        var analyzer = new ArchitectureAnalyzer([new StubRule(criticalCount, warningCount)]);

        var result = analyzer.Analyze(EmptyGraph);

        Assert.Equal(expectedScore, result.Score);
    }

    [Fact]
    public void Analyze_ClampsScoreAtZero()
    {
        var analyzer = new ArchitectureAnalyzer([new StubRule(criticalCount: 10, warningCount: 0)]);

        var result = analyzer.Analyze(EmptyGraph);

        Assert.Equal(0, result.Score);
    }

    private sealed class StubRule(int criticalCount, int warningCount) : IArchitectureRule
    {
        public string Code => "STUB";
        public Severity Severity => Severity.Warning;

        public IEnumerable<RuleFinding> Evaluate(ArchitectureGraph graph)
        {
            for (var i = 0; i < criticalCount; i++)
                yield return new RuleFinding { Code = "STUB_CRIT", Severity = Severity.Critical, Message = "critical" };

            for (var i = 0; i < warningCount; i++)
                yield return new RuleFinding { Code = "STUB_WARN", Severity = Severity.Warning, Message = "warning" };
        }
    }
}
