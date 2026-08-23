using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

/// <summary>
/// QA §4. The asynchronous twin of ORPHAN_NODE: a queue somebody publishes to and nobody drains.
/// </summary>
public class QueueNoConsumerRuleTests
{
    private readonly QueueNoConsumerRule _rule = new();
    private readonly GraphBuilder _builder = new();

    [Fact]
    public void A_queue_with_a_producer_and_no_consumer_is_flagged()
    {
        // The half of the pattern this product recommends, without the other half. Before QA §4
        // this scored exactly the same as the whole pattern.
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cola", NodeType.Queue)
            .Connect("Api", "Cola", synchronous: false, protocol: ProtocolType.Amqp);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(RuleCodes.QueueNoConsumer, finding.Code);
        Assert.Equal(Area.Coupling, finding.Area);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.QUEUE_NO_CONSUMER.unconsumed.message(node=Cola)", finding.Message.Describe());
        Assert.Equal("finding.QUEUE_NO_CONSUMER.unconsumed.explanation", finding.Explanation.Key());
        Assert.Equal("finding.QUEUE_NO_CONSUMER.unconsumed.suggestion", finding.Suggestion.Key());
        Assert.Equal(_builder.NodeElement("Cola"), Assert.Single(finding.Elements));
        Assert.Null(finding.Fix);
    }

    [Fact]
    public void The_edge_that_leaves_the_queue_is_the_consumer()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cola", NodeType.Queue)
            .Node("Worker", NodeType.Worker)
            .Connect("Api", "Cola", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Cola", "Worker", synchronous: false, protocol: ProtocolType.Amqp);

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void A_worker_pointing_at_the_queue_is_publishing_and_the_card_names_the_arrow()
    {
        // The drawing the convention turns upside down: the user meant "Worker consumes from Cola"
        // and drew "Worker depends on Cola", which to the load model, the simulator and this rule
        // is a second publisher. The finding says so, and points at the arrow to reverse.
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cola", NodeType.Queue)
            .Node("Worker", NodeType.Worker)
            .Connect("Api", "Cola", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Worker", "Cola", synchronous: false, protocol: ProtocolType.Amqp);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.QUEUE_NO_CONSUMER.backwards.explanation(node=Cola, worker=Worker)", finding.Explanation.Describe());
        Assert.Equal("finding.QUEUE_NO_CONSUMER.backwards.suggestion(node=Cola, worker=Worker)", finding.Suggestion.Describe());
        Assert.Equal([_builder.NodeElement("Cola"), _builder.EdgeElement("Worker", "Cola")], finding.Elements);
    }

    [Fact]
    public void A_queue_delivering_to_itself_has_no_consumer()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cola", NodeType.Queue)
            .Connect("Api", "Cola", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Cola", "Cola", synchronous: false, protocol: ProtocolType.Amqp);

        Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void An_orphan_queue_is_ORPHAN_NODEs_finding_and_not_this_ones()
    {
        // QA B4: a box in no flow is one finding, under one code. With nobody publishing there is
        // nothing to drain, so the rule has nothing to look at.
        _builder
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Node("Cola", NodeType.Queue)
            .Connect("Api", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }

    [Fact]
    public void Without_a_queue_there_is_nothing_to_check()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }
}
