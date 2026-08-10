using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Fixtures;

/// <summary>
/// The precedences of D4.5 and the fact-passing of D5.1, over the seven real rules. None of these
/// can be proved rule by rule: they are about what two rules do to each other.
/// </summary>
public class RulePrecedenceTests
{
    private readonly GraphBuilder _builder = new();

    private static ArchitectureAnalyzer Analyzer() => new([
        new SpofRule(),
        new CycleRule(),
        new RetryAmplificationRule(),
        new SyncDependencyChainRule(),
        new MissingResilienceRule(),
        new HighCouplingRule(),
        new MissingCacheRule()
    ]);

    [Fact]
    public void RetryAmplificationAndMissingResilience_NeverShareAnEdge()
    {
        // Both would say "this call has no timeout". The more specific one speaks (D4.5).
        _builder
            .Node("Gateway", NodeType.Gateway)
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Gateway", "Orders", timeoutSeconds: 5)
            .Connect("Orders", "Payments", retries: 3);

        var result = Analyzer().Analyze(_builder.Build());
        var edge = FindingElement.Connection(_builder.EdgeId("Orders", "Payments"));

        Assert.Contains(result.Findings, f => f.Code == RuleCodes.RetryAmplification && f.Elements.Contains(edge));
        Assert.DoesNotContain(result.Findings, f => f.Code == RuleCodes.MissingResilience && f.Elements.Contains(edge));
    }

    [Fact]
    public void CycleAndSyncChain_NeverShareAnEdge()
    {
        // A cycle reported again as a chain is the same fact under two codes (D4.5) - and it is
        // what excluding those edges buys SYNC_CHAIN its acyclic graph (D5.4).
        _builder
            .Nodes(NodeType.Service, "Orders", "Payments", "Inventory")
            .Connect("Orders", "Payments", timeoutSeconds: 5)
            .Connect("Payments", "Inventory", timeoutSeconds: 5)
            .Connect("Inventory", "Orders", timeoutSeconds: 5);

        var result = Analyzer().Analyze(_builder.Build());

        var cyclicEdges = result.Findings
            .Where(f => f.Code == RuleCodes.Cycle)
            .SelectMany(f => f.Elements)
            .Where(e => e.Type == ElementType.Connection)
            .ToHashSet();

        Assert.NotEmpty(cyclicEdges);
        Assert.DoesNotContain(result.Findings, f =>
            f.Code == RuleCodes.SyncChain && f.Elements.Any(cyclicEdges.Contains));
    }

    [Fact]
    public void MissingResilienceEscalatesOnlyTowardsACriticalSpof()
    {
        // Db has three dependents (Critical), Cache has two (Warning). The calls towards Db
        // escalate; the ones towards the cache do not - which is what stops a correct
        // architecture with a shared cache from filling up with criticals (D4.4, correction v1.1).
        _builder
            .Node("Db", NodeType.Database)
            .Node("Cache", NodeType.Cache)
            .Nodes(NodeType.Service, "Orders", "Payments", "Users")
            .Connect("Orders", "Db", timeoutSeconds: 5)
            .Connect("Payments", "Db", timeoutSeconds: 5)
            .Connect("Users", "Db", timeoutSeconds: 5)
            .Connect("Orders", "Cache", timeoutSeconds: 5)
            .Connect("Payments", "Cache", timeoutSeconds: 5);

        var result = Analyzer().Analyze(_builder.Build());
        var escalated = result.Findings
            .Where(f => f.Code == RuleCodes.MissingResilience && f.Severity == Severity.Critical)
            .SelectMany(f => f.Elements)
            .ToList();

        Assert.Equal(3, escalated.Count);
        Assert.DoesNotContain(FindingElement.Connection(_builder.EdgeId("Orders", "Cache")), escalated);
    }

    [Fact]
    public void ConsumersSeeTheFactsWhateverTheRegistrationOrder()
    {
        // The two-phase engine at work over the real rules: MISSING_RESILIENCE runs after SPOF
        // even when it is registered first (D5.2).
        _builder
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Service, "Orders", "Payments", "Users")
            .Connect("Orders", "Db", timeoutSeconds: 5)
            .Connect("Payments", "Db", timeoutSeconds: 5)
            .Connect("Users", "Db", timeoutSeconds: 5);

        var reversed = new ArchitectureAnalyzer([
            new MissingCacheRule(),
            new HighCouplingRule(),
            new MissingResilienceRule(),
            new SyncDependencyChainRule(),
            new RetryAmplificationRule(),
            new CycleRule(),
            new SpofRule()
        ]);

        var result = reversed.Analyze(_builder.Build());

        Assert.Equal(3, result.Findings.Count(f =>
            f.Code == RuleCodes.MissingResilience && f.Severity == Severity.Critical));
    }

    [Fact]
    public void EmptyArchitecture_ScoresNullInsteadOfOneHundred()
    {
        // §11: no rule had anything to check, so no area is Scored. A blank canvas is not a
        // perfect architecture (D1.2, rule 4).
        var result = Analyzer().Analyze(new ArchitectureGraph([], []));

        Assert.Null(result.Score);
        Assert.DoesNotContain(result.Areas, area => area.State == AreaState.Scored);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void SingleUnconnectedNode_ScoresNullInsteadOfOneHundred()
    {
        // The autosave fires on intermediate states: the node you just dragged must not produce a
        // score out of nothing.
        _builder.Node("Api", NodeType.Api);

        var result = Analyzer().Analyze(_builder.Build());

        Assert.Null(result.Score);
        Assert.Empty(result.Findings);
    }
}
