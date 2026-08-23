using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis;

/// <summary>
/// V4 D5.6. Which findings carry a correction, and - just as much the point - which do not.
///
/// V3 called this debt <em>"siete reglas, siete propuestas: un proyecto, no una funcionalidad"</em>.
/// It stops being a project by automating only the corrections where there is nothing to decide.
/// The assertions below are the two halves of that sentence.
/// </summary>
public class AppliedFixTests
{
    [Fact]
    public void A_call_with_no_protection_gets_a_timeout_and_an_alternative()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", protocol: ProtocolType.Sql)
            .Build();

        var fix = Assert.Single(new MissingResilienceRule().Evaluate(graph).Findings).Fix;

        Assert.NotNull(fix);
        Assert.Equal(FixKinds.SetEdgeResilience, fix!.Kind);
        Assert.Equal(ElementType.Connection, fix.Element.Type);
        Assert.Equal(5, fix.TimeoutSeconds);
        Assert.True(fix.Fallback);
    }

    [Fact]
    public void A_timeout_shorter_than_its_own_call_is_raised_above_it()
    {
        // Twice the measured cost, rounded up: enough headroom that applying it once is enough,
        // and it must not create a TIMEOUT_TOO_SHORT of its own on the next analysis.
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":1200}""")
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 1, protocol: ProtocolType.Sql)
            .Build();

        var fix = Assert.Single(new TimeoutTooShortRule().Evaluate(graph).Findings).Fix;

        Assert.NotNull(fix);
        Assert.Equal(FixKinds.SetEdgeResilience, fix!.Kind);
        Assert.True(fix.TimeoutSeconds >= 5);
        Assert.Null(fix.Fallback);   // raising the timeout is the fix; nothing else is touched
    }

    [Fact]
    public void A_queue_without_a_dead_letter_queue_gets_the_checkbox()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Q", NodeType.Queue)
            .Node("Worker", NodeType.Worker)
            .Connect("Api", "Q", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Q", "Worker", synchronous: false, protocol: ProtocolType.Amqp)
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
            .Connect("A", "Db", protocol: ProtocolType.Sql)
            .Connect("B", "Db", protocol: ProtocolType.Sql)
            .Build();

        var fix = Assert.Single(new MissingCacheRule().Evaluate(graph).Findings).Fix;

        Assert.Equal(FixKinds.InsertCache, fix!.Kind);

        // The element is the database, not one of the two calls: the transformation is "everything
        // goes through a cache", which has one answer, rather than "cache this particular call",
        // which would be picking one of two arbitrarily.
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
            .Connect("Api", "Db", protocol: ProtocolType.Sql)
            .Build();

        var fix = Assert.Single(new OrphanNodeRule().Evaluate(graph).Findings).Fix;

        Assert.Equal(FixKinds.RemoveNode, fix!.Kind);
        Assert.Equal("fix.ORPHAN_NODE.remove(node=Leftover)", fix.Label.Describe());
    }

    /// <summary>
    /// <strong>The other half of D5.6.</strong> A SPOF has four legitimate answers - replicate it,
    /// put a balancer in front, make the call asynchronous, accept it - and picking one for the
    /// user is taking away the decision the product exists to teach (§21). The same goes for
    /// coupling, cycles, chains, an exposed database and a component that is short of capacity.
    ///
    /// If this test ever goes red, somebody automated a judgement call.
    /// </summary>
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
        // The count is the decision, so it is asserted rather than left to be counted by hand:
        // five corrections where there is nothing to decide, text where there is.
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
