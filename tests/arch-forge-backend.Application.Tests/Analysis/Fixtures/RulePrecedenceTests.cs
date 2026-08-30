using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Fixtures;

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
        var result = Analyzer().Analyze(new ArchitectureGraph([], []));

        Assert.Null(result.Score);
        Assert.DoesNotContain(result.Areas, area => area.State == AreaState.Scored);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void SingleUnconnectedNode_ScoresNullInsteadOfOneHundred()
    {
        _builder.Node("Api", NodeType.Api);

        var result = Analyzer().Analyze(_builder.Build());

        Assert.Null(result.Score);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void ProtocolMismatchAndAsyncNoQueue_NeverShareAnEdge()
    {
        _builder
            .Nodes(NodeType.Service, "Orders", "Mailer")
            .Connect("Orders", "Mailer", synchronous: false, protocol: ProtocolType.Amqp);

        var result = new ArchitectureAnalyzer([new ProtocolMismatchRule(), new AsyncNoQueueRule()]).Analyze(_builder.Build());
        var edge = FindingElement.Connection(_builder.EdgeId("Orders", "Mailer"));

        Assert.Contains(result.Findings, f => f.Code == RuleCodes.ProtocolMismatch && f.Elements.Contains(edge));
        Assert.DoesNotContain(result.Findings, f => f.Code == RuleCodes.AsyncNoQueue && f.Elements.Contains(edge));
    }

    // The fix MISSING_RESILIENCE offers on a telemetry push is to stop waiting. Applying it must not
    // hand the same edge to the rule that asks where the work waits: telemetry is not work.
    [Fact]
    public void MakingTheTelemetryPushAsynchronous_DoesNotHandTheEdgeToAsyncNoQueue()
    {
        var analyzer = new ArchitectureAnalyzer([new MissingResilienceRule(), new AsyncNoQueueRule()]);

        _builder
            .Node("Api", NodeType.Api).Node("Metrics", NodeType.Observability)
            .Connect("Api", "Metrics");

        var push = Assert.Single(analyzer.Analyze(_builder.Build()).Findings, f => f.Code == RuleCodes.MissingResilience);
        Assert.Equal(FixKinds.MakeEdgeAsynchronous, push.Fix!.Kind);

        var applied = analyzer.Analyze(new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Metrics", NodeType.Observability)
            .Connect("Api", "Metrics", synchronous: false)
            .Build());

        Assert.DoesNotContain(applied.Findings, f => f.Code == RuleCodes.AsyncNoQueue);
    }
}
