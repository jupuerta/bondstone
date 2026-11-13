using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Analysis.Rules;
using bondstone_backend.Application.Tests.Analysis.Rules.Support;
using bondstone_backend.Application.Tests.TestDoubles;
using bondstone_backend.Domain.Enums;
using Xunit;

namespace bondstone_backend.Application.Tests.Analysis;

public class AppliedFixTests
{
    [Fact]
    public void A_call_with_no_protection_gets_a_timeout_and_an_alternative()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db")
            .Build();

        var fix = Assert.Single(new MissingResilienceRule().Evaluate(graph).Findings).Fix;

        Assert.NotNull(fix);
        Assert.Equal(FixKinds.SetEdgeResilience, fix!.Kind);
        Assert.Equal(ElementType.Connection, fix.Element.Type);
        Assert.Equal(5, fix.TimeoutSeconds);
        Assert.True(fix.Fallback);
    }

    [Fact]
    public void A_synchronous_send_to_the_telemetry_is_made_asynchronous()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Obs")
            .Build();

        var fix = Assert.Single(new MissingResilienceRule().Evaluate(graph).Findings).Fix;

        Assert.NotNull(fix);
        Assert.Equal(FixKinds.MakeEdgeAsynchronous, fix!.Kind);
        Assert.Equal(ElementType.Connection, fix.Element.Type);
        Assert.Equal("fix.MISSING_RESILIENCE.makeAsynchronous", fix.Label.Describe());
        Assert.Null(fix.TimeoutSeconds);
        Assert.Null(fix.Fallback);
    }

    // QA 30-08 §10.3, point 12: this rule used to offer «raise the timeout», which is what its own
    // challenge teaches not to do — what makes the call slower than its timeout is the queue at the
    // destination, and a bigger number only buys a slower answer. The sentence asks for capacity now.
    [Fact]
    public void A_timeout_shorter_than_its_own_call_asks_for_capacity_and_carries_no_button()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":1200}""")
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 1)
            .Build();

        var finding = Assert.Single(new TimeoutTooShortRule().Evaluate(graph).Findings);

        Assert.Null(finding.Fix);
        Assert.Equal("finding.TIMEOUT_TOO_SHORT.expires.suggestion", finding.Suggestion.Key());
    }

    [Fact]
    public void A_queue_without_a_dead_letter_queue_gets_the_checkbox()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Q", NodeType.Queue)
            .Node("Worker", NodeType.Worker)
            .Connect("Api", "Q", synchronous: false)
            .Connect("Q", "Worker", synchronous: false)
            .Build();

        var fix = Assert.Single(new QueueNoDlqRule().Evaluate(graph).Findings).Fix;

        Assert.Equal(FixKinds.SetNodeFlag, fix!.Kind);
        Assert.Equal("deadLetterQueue", fix.ConfigurationKey);
        Assert.True(fix.ConfigurationValue);
    }

    [Fact]
    public void A_database_everybody_queries_gets_a_cache_in_front()
    {
        var graph = new GraphBuilder()
            .Node("A", NodeType.Service)
            .Node("B", NodeType.Service)
            .Node("Db", NodeType.Database)
            .Connect("A", "Db")
            .Connect("B", "Db")
            .Build();

        var fix = Assert.Single(new MissingCacheRule().Evaluate(graph).Findings).Fix;

        Assert.Equal(FixKinds.InsertCache, fix!.Kind);

        Assert.Equal(ElementType.Node, fix.Element.Type);
        Assert.Equal("fix.MISSING_CACHE.insert(node=Db)", fix.Label.Describe());
    }

    [Fact]
    public void A_component_connected_to_nothing_gets_a_delete_button()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Node("Leftover", NodeType.Service)
            .Connect("Api", "Db")
            .Build();

        var fix = Assert.Single(new OrphanNodeRule().Evaluate(graph).Findings).Fix;

        Assert.Equal(FixKinds.RemoveNode, fix!.Kind);
        Assert.Equal("fix.ORPHAN_NODE.remove(node=Leftover)", fix.Label.Describe());
    }

    [Fact]
    public void The_rules_whose_fix_is_a_decision_carry_no_button()
    {
        var graph = ReferenceArchitectureFor("worst-case");

        var automated = graph.Findings
            .Where(f => f.Fix is not null)
            .Select(f => f.Code)
            .Distinct()
            .Order()
            .ToList();

        Assert.DoesNotContain(RuleCodes.Spof, automated);
        Assert.DoesNotContain(RuleCodes.HighCoupling, automated);
        Assert.DoesNotContain(RuleCodes.Cycle, automated);
        Assert.DoesNotContain(RuleCodes.SyncChain, automated);
        Assert.DoesNotContain(RuleCodes.ExposedDatabase, automated);
        Assert.DoesNotContain(RuleCodes.UndersizedComponent, automated);
        Assert.DoesNotContain(RuleCodes.RetryAmplification, automated);
    }

    [Fact]
    public void Exactly_five_of_the_fifteen_rules_can_carry_one()
    {
        string[] withFix =
        [
            RuleCodes.MissingResilience, RuleCodes.TimeoutTooShort, RuleCodes.QueueNoDlq,
            RuleCodes.MissingCache, RuleCodes.OrphanNode
        ];

        Assert.Equal(5, withFix.Length);
    }

    private static AnalysisResult ReferenceArchitectureFor(string fixture) =>
        Fixtures.ReferenceArchitecture.Analyze(fixture);
}
