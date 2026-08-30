using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

public class AsyncNoQueueRuleTests
{
    private static RuleResult Evaluate(GraphBuilder builder) => new AsyncNoQueueRule().Evaluate(builder.Build());

    private static GraphBuilder FireAndForget() => new GraphBuilder()
        .Nodes(NodeType.Service, "Orders", "Mailer")
        .Connect("Orders", "Mailer", synchronous: false);

    [Fact]
    public void An_http_call_that_does_not_wait_and_has_nowhere_to_wait_is_a_warning()
    {
        var builder = FireAndForget();

        var result = Evaluate(builder);

        Assert.True(result.IsApplicable);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(Area.Resilience, finding.Area);
        Assert.Equal("finding.ASYNC_NO_QUEUE.unbuffered.message(source=Orders, target=Mailer)", finding.Message.Describe());
        Assert.Equal("finding.ASYNC_NO_QUEUE.unbuffered.explanation(source=Orders, target=Mailer)", finding.Explanation.Describe());
        Assert.Equal("finding.ASYNC_NO_QUEUE.unbuffered.suggestion(source=Orders, target=Mailer)", finding.Suggestion!.Describe());
        Assert.Equal([builder.EdgeElement("Orders", "Mailer")], finding.Elements);
    }

    [Fact]
    public void The_fix_puts_a_queue_on_the_call_itself()
    {
        var builder = FireAndForget();

        var fix = Assert.Single(Evaluate(builder).Findings).Fix;

        Assert.NotNull(fix);
        Assert.Equal(FixKinds.InsertQueue, fix!.Kind);
        Assert.Equal(builder.EdgeElement("Orders", "Mailer"), fix.Element);
        Assert.Equal("fix.ASYNC_NO_QUEUE.insert(source=Orders, target=Mailer)", fix.Label.Describe());
    }

    [Theory]
    [InlineData(NodeType.Api, NodeType.Service)]
    [InlineData(NodeType.Worker, NodeType.Api)]
    [InlineData(NodeType.Service, NodeType.Worker)]
    [InlineData(NodeType.Service, NodeType.ExternalService)]
    public void Whoever_works_and_hands_work_over_without_waiting_gets_the_same_lesson(NodeType source, NodeType target)
    {
        var result = Evaluate(new GraphBuilder()
            .Node("From", source).Node("To", target)
            .Connect("From", "To", synchronous: false));

        Assert.Single(result.Findings);
    }

    [Fact]
    public void Publishing_into_a_queue_passes_and_so_does_a_topic()
    {
        var result = Evaluate(new GraphBuilder()
            .Node("Orders", NodeType.Service).Node("Emails", NodeType.Queue).Node("Mailer", NodeType.Worker)
            .Node("Events", NodeType.Topic).Node("Audit", NodeType.Service)
            .Connect("Orders", "Emails", synchronous: false)
            .Connect("Emails", "Mailer", synchronous: false)
            .Connect("Orders", "Events", synchronous: false)
            .Connect("Events", "Audit", synchronous: false));

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void One_queue_does_not_excuse_the_call_that_goes_around_it()
    {
        var builder = new GraphBuilder()
            .Node("Orders", NodeType.Service).Node("Emails", NodeType.Queue).Node("Mailer", NodeType.Worker)
            .Node("Stock", NodeType.Service)
            .Connect("Orders", "Emails", synchronous: false)
            .Connect("Emails", "Mailer", synchronous: false)
            .Connect("Orders", "Stock", synchronous: false);

        var finding = Assert.Single(Evaluate(builder).Findings);

        Assert.Equal([builder.EdgeElement("Orders", "Stock")], finding.Elements);
    }

    [Fact]
    public void A_telemetry_push_made_asynchronous_is_the_fix_not_the_next_finding()
    {
        var result = Evaluate(new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Metrics", NodeType.Observability)
            .Connect("Api", "Metrics", synchronous: false));

        Assert.False(result.IsApplicable);
        Assert.Equal("rule.na.ASYNC_NO_QUEUE.noAsyncCall", result.NotApplicableReason!.Key());
    }

    [Fact]
    public void What_a_client_or_a_third_party_does_not_wait_for_is_not_your_call()
    {
        var result = Evaluate(new GraphBuilder()
            .Node("Users", NodeType.Client).Node("Webhooks", NodeType.ExternalService).Node("Api", NodeType.Api)
            .Connect("Users", "Api", synchronous: false)
            .Connect("Webhooks", "Api", synchronous: false));

        Assert.False(result.IsApplicable);
    }

    [Theory]
    [InlineData(NodeType.Gateway, NodeType.Service)]
    [InlineData(NodeType.LoadBalancer, NodeType.Api)]
    [InlineData(NodeType.Scheduler, NodeType.Worker)]
    [InlineData(NodeType.Service, NodeType.Cache)]
    [InlineData(NodeType.Service, NodeType.Database)]
    [InlineData(NodeType.Worker, NodeType.ObjectStorage)]
    public void Routing_a_clock_and_a_deferred_write_are_other_lessons(NodeType source, NodeType target)
    {
        var result = Evaluate(new GraphBuilder()
            .Node("From", source).Node("To", target)
            .Connect("From", "To", synchronous: false));

        Assert.False(result.IsApplicable);
    }

    [Fact]
    public void Waiting_is_the_other_rules_business()
    {
        var result = Evaluate(new GraphBuilder()
            .Nodes(NodeType.Service, "Orders", "Mailer")
            .Connect("Orders", "Mailer", timeoutSeconds: 3));

        Assert.False(result.IsApplicable);
        Assert.Equal("rule.na.ASYNC_NO_QUEUE.noAsyncCall", result.NotApplicableReason!.Key());
    }
}
