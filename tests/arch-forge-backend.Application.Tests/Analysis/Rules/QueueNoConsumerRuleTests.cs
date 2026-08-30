using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

public class QueueNoConsumerRuleTests
{
    private readonly QueueNoConsumerRule _rule = new();
    private readonly GraphBuilder _builder = new();

    [Fact]
    public void A_queue_with_a_producer_and_no_consumer_is_flagged()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cola", NodeType.Queue)
            .Connect("Api", "Cola", synchronous: false);

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
            .Connect("Api", "Cola", synchronous: false)
            .Connect("Cola", "Worker", synchronous: false);

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void A_worker_pointing_at_the_queue_is_publishing_and_the_card_names_the_arrow()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cola", NodeType.Queue)
            .Node("Worker", NodeType.Worker)
            .Connect("Api", "Cola", synchronous: false)
            .Connect("Worker", "Cola", synchronous: false);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.QUEUE_NO_CONSUMER.backwards.explanation(node=Cola, worker=Worker)", finding.Explanation.Describe());
        Assert.Equal("finding.QUEUE_NO_CONSUMER.backwards.suggestion(node=Cola, worker=Worker)", finding.Suggestion.Describe());
        Assert.Equal([_builder.NodeElement("Cola"), _builder.EdgeElement("Worker", "Cola")], finding.Elements);
    }


    [Fact]
    public void A_database_at_the_end_of_the_queue_is_not_a_consumer()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cola", NodeType.Queue)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Cola", synchronous: false)
            .Connect("Cola", "Db", synchronous: false);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.QUEUE_NO_CONSUMER.unconsumed.explanation", finding.Explanation.Key());
        Assert.Equal(_builder.NodeElement("Cola"), Assert.Single(finding.Elements));
    }

    [Fact]
    public void A_false_consumer_no_longer_hides_the_worker_drawn_backwards()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cola", NodeType.Queue)
            .Node("Db", NodeType.Database)
            .Node("Worker", NodeType.Worker)
            .Connect("Api", "Cola", synchronous: false)
            .Connect("Cola", "Db", synchronous: false)
            .Connect("Worker", "Cola", synchronous: false);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.QUEUE_NO_CONSUMER.backwards.explanation(node=Cola, worker=Worker)", finding.Explanation.Describe());
        Assert.Equal([_builder.NodeElement("Cola"), _builder.EdgeElement("Worker", "Cola")], finding.Elements);
    }

    [Theory]
    [InlineData(NodeType.Worker)]
    [InlineData(NodeType.Service)]
    [InlineData(NodeType.Api)]
    [InlineData(NodeType.ExternalService)]
    public void Whatever_runs_code_on_a_message_consumes(NodeType consumer)
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cola", NodeType.Queue)
            .Node("Consumer", consumer)
            .Connect("Api", "Cola", synchronous: false)
            .Connect("Cola", "Consumer", synchronous: false);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Theory]
    [InlineData(NodeType.Database)]
    [InlineData(NodeType.Cache)]
    [InlineData(NodeType.ObjectStorage)]
    [InlineData(NodeType.Client)]
    [InlineData(NodeType.LoadBalancer)]
    [InlineData(NodeType.IdentityProvider)]
    public void Whatever_does_not_run_code_on_a_message_does_not(NodeType target)
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cola", NodeType.Queue)
            .Node("Target", target)
            .Connect("Api", "Cola", synchronous: false)
            .Connect("Cola", "Target", synchronous: false);

        Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void A_queue_forwarding_to_a_drained_queue_has_a_consumer_at_every_stage()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Nodes(NodeType.Queue, "Cola1", "Cola2")
            .Node("Worker", NodeType.Worker)
            .Connect("Api", "Cola1", synchronous: false)
            .Connect("Cola1", "Cola2", synchronous: false)
            .Connect("Cola2", "Worker", synchronous: false);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void A_chain_of_queues_nobody_drains_is_flagged_at_every_stage()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Nodes(NodeType.Queue, "Cola1", "Cola2")
            .Connect("Api", "Cola1", synchronous: false)
            .Connect("Cola1", "Cola2", synchronous: false);

        Assert.Equal(2, _rule.Evaluate(_builder.Build()).Findings.Count);
    }

    [Fact]
    public void Two_queues_forwarding_to_each_other_drain_nothing()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Nodes(NodeType.Queue, "Cola1", "Cola2")
            .Connect("Api", "Cola1", synchronous: false)
            .Connect("Cola1", "Cola2", synchronous: false)
            .Connect("Cola2", "Cola1", synchronous: false);

        Assert.Equal(2, _rule.Evaluate(_builder.Build()).Findings.Count);
    }

    [Fact]
    public void A_queue_delivering_to_itself_has_no_consumer()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cola", NodeType.Queue)
            .Connect("Api", "Cola", synchronous: false)
            .Connect("Cola", "Cola", synchronous: false);

        Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void An_orphan_queue_is_ORPHAN_NODEs_finding_and_not_this_ones()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Node("Cola", NodeType.Queue)
            .Connect("Api", "Db", timeoutSeconds: 5);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }

    [Fact]
    public void Without_a_queue_there_is_nothing_to_check()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 5);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }
}
