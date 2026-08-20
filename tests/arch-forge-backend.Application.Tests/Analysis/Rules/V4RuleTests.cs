using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

/// <summary>
/// V4 D5.2, the eight rules that pay the debt V2 wrote down and V3 pushed twice - one section each,
/// over graphs small enough that what is being asserted is obvious.
/// </summary>
public class OrphanNodeRuleTests
{
    [Fact]
    public void Flags_a_component_nobody_calls_and_that_calls_nobody()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Node("Leftover", NodeType.Service)
            .Connect("Api", "Db", protocol: ProtocolType.Sql)
            .Build();

        var result = new OrphanNodeRule().Evaluate(graph);

        Assert.True(result.IsApplicable);
        var finding = Assert.Single(result.Findings);
        Assert.Contains("'Leftover'", finding.Message.Es());
    }

    [Fact]
    public void A_single_component_is_unfinished_rather_than_wrong()
    {
        // Firing on the first thing anybody drags onto the canvas is how an analyzer becomes noise.
        var graph = new GraphBuilder().Node("Api", NodeType.Api).Build();

        Assert.False(new OrphanNodeRule().Evaluate(graph).IsApplicable);
    }
}

public class QueueNoDlqRuleTests
{
    [Fact]
    public void Flags_a_queue_with_nowhere_to_put_what_it_cannot_process()
    {
        var graph = Queue("{}");
        var result = new QueueNoDlqRule().Evaluate(graph);

        Assert.True(result.IsApplicable);
        Assert.Contains("dead letter queue", Assert.Single(result.Findings).Message.Es());
    }

    [Fact]
    public void The_field_that_came_back_with_this_rule_silences_it()
    {
        // V3 D6.6 promised DeadLetterQueue would come back "with the rule that justifies it".
        // This is the assertion that the two arrived together.
        Assert.Empty(new QueueNoDlqRule().Evaluate(Queue("""{"deadLetterQueue":true}""")).Findings);
    }

    [Fact]
    public void Without_a_queue_there_is_nothing_to_check()
    {
        var graph = new GraphBuilder().Node("Api", NodeType.Api).Node("Db", NodeType.Database).Build();

        Assert.False(new QueueNoDlqRule().Evaluate(graph).IsApplicable);
    }

    private static ArchitectureGraph Queue(string configuration) => new GraphBuilder()
        .Node("Api", NodeType.Api)
        .Node("Q", NodeType.Queue, configuration)
        .Connect("Api", "Q", synchronous: false, protocol: ProtocolType.Amqp)
        .Build();
}

public class TimeoutTooShortRuleTests
{
    [Fact]
    public void Traffic_is_what_makes_a_timeout_too_short()
    {
        // **The reason this rule needed V4 and not just V3.** Timeouts are whole seconds and an
        // idle architecture answers in tens of milliseconds, so a timeout shorter than an idle call
        // is almost unreachable - the rule would be dead code. Under load it is a different
        // question: the database at six times its capacity costs 2,4 s, and the 1 s timeout in
        // front of it expires every single time while every component is still "up".
        var idle = ApiAndDb(0);
        var loaded = ApiAndDb(1_200);

        Assert.Empty(new TimeoutTooShortRule().Evaluate(idle).Findings);

        var finding = Assert.Single(new TimeoutTooShortRule().Evaluate(loaded).Findings);
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Contains("más corto que la propia llamada", finding.Message.Es());
    }

    [Fact]
    public void It_needs_CallCost_which_is_why_V2_could_not_have_it()
    {
        // The debt was not deferred out of laziness: knowing what a call costs is exactly what V3
        // wrote CallCost for. The suggestion quotes that figure back.
        var finding = Assert.Single(new TimeoutTooShortRule().Evaluate(ApiAndDb(1_200)).Findings);

        Assert.Contains("Sube el timeout", finding.Suggestion.Es());
    }

    private static ArchitectureGraph ApiAndDb(int rps) => new GraphBuilder()
        .Node("Api", NodeType.Api, rps == 0 ? "{}" : $$"""{"requestsPerSecond":{{rps}}}""")
        .Node("Db", NodeType.Database)
        .Connect("Api", "Db", timeoutSeconds: 1, protocol: ProtocolType.Sql)
        .Build();

    [Fact]
    public void Without_a_single_timeout_there_is_nothing_to_compare()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", protocol: ProtocolType.Sql)
            .Build();

        Assert.False(new TimeoutTooShortRule().Evaluate(graph).IsApplicable);
    }
}

public class UndersizedComponentRuleTests
{
    [Fact]
    public void Says_nothing_until_somebody_declares_how_much_traffic_comes_in()
    {
        // DT.1: with no declared load there is no utilisation, so there is nothing to be
        // under-sized for. Every architecture drawn before V4 sees this as "not applicable".
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", protocol: ProtocolType.Sql)
            .Build();

        Assert.False(new UndersizedComponentRule().Evaluate(graph).IsApplicable);
    }

    [Fact]
    public void Over_capacity_is_critical_and_close_to_it_is_a_warning()
    {
        // Db takes 200 rps. At 300 it is rejecting; at 180 it is one peak away.
        var overloaded = Loaded(300);
        var tight = Loaded(180);

        Assert.Equal(
            Severity.Critical,
            Assert.Single(new UndersizedComponentRule().Evaluate(overloaded).Findings).Severity);

        Assert.Equal(
            Severity.Warning,
            Assert.Single(new UndersizedComponentRule().Evaluate(tight).Findings).Severity);
    }

    [Fact]
    public void The_message_carries_the_two_numbers_that_let_you_fix_it()
    {
        var finding = Assert.Single(new UndersizedComponentRule().Evaluate(Loaded(300)).Findings);

        Assert.Contains("300", finding.Explanation.Es());
        Assert.Contains("200", finding.Explanation.Es());
        Assert.Contains("caché", finding.Suggestion.Es());   // it is a database, so that is the advice
    }

    private static ArchitectureGraph Loaded(int rps) => new GraphBuilder()
        .Node("Api", NodeType.Api, $$"""{"requestsPerSecond":{{rps}}}""")
        .Node("Db", NodeType.Database)
        .Connect("Api", "Db", protocol: ProtocolType.Sql)
        .Build();
}

public class SecurityRuleTests
{
    [Fact]
    public void An_unauthenticated_entry_point_with_a_database_behind_it_is_critical()
    {
        var finding = Assert.Single(new UnauthenticatedEntryRule().Evaluate(ApiAndDb("{}")).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Contains("'Api'", finding.Message.Es());
    }

    [Fact]
    public void Authentication_silences_it()
    {
        Assert.Empty(new UnauthenticatedEntryRule().Evaluate(ApiAndDb("""{"authentication":true}""")).Findings);
    }

    [Fact]
    public void With_no_data_behind_the_door_there_is_nothing_to_protect()
    {
        // Without this, every new diagram would open with a red critical before the user had
        // finished drawing it - the "todo rojo" this project has spent three versions avoiding.
        var sketch = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Cache", NodeType.Cache)
            .Connect("Api", "Cache")
            .Build();

        Assert.False(new UnauthenticatedEntryRule().Evaluate(sketch).IsApplicable);
    }

    [Fact]
    public void Everything_a_balancer_fronts_is_public_too()
    {
        // Traffic entering through a balancer reaches the APIs behind it without meeting anything
        // that could ask who is calling, so they are as exposed as the balancer is.
        var graph = new GraphBuilder()
            .Node("LB", NodeType.LoadBalancer)
            .Node("Api1", NodeType.Api)
            .Node("Api2", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("LB", "Api1")
            .Connect("LB", "Api2")
            .Connect("Api1", "Db", protocol: ProtocolType.Sql)
            .Connect("Api2", "Db", protocol: ProtocolType.Sql)
            .Build();

        Assert.Equal(2, new UnauthenticatedEntryRule().Evaluate(graph).Findings.Count);
    }

    [Fact]
    public void A_database_reachable_without_passing_through_an_api_is_exposed()
    {
        var graph = new GraphBuilder()
            .Node("LB", NodeType.LoadBalancer)
            .Node("Db", NodeType.Database)
            .Connect("LB", "Db", protocol: ProtocolType.Sql)
            .Build();

        var finding = Assert.Single(new ExposedDatabaseRule().Evaluate(graph).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Contains("sin pasar por ninguna API", finding.Message.Es());
    }

    [Fact]
    public void A_database_behind_an_api_is_not()
    {
        Assert.Empty(new ExposedDatabaseRule().Evaluate(ApiAndDb("{}")).Findings);
    }

    [Fact]
    public void A_gateway_admitting_declared_traffic_with_no_limit_is_flagged()
    {
        var unlimited = Gateway("""{"requestsPerSecond":1000}""");
        var limited = Gateway("""{"requestsPerSecond":1000,"rateLimit":500}""");

        Assert.Single(new NoRateLimitRule().Evaluate(unlimited).Findings);
        Assert.Empty(new NoRateLimitRule().Evaluate(limited).Findings);
    }

    [Fact]
    public void A_rate_limit_defends_against_a_volume_so_it_needs_one_declared()
    {
        Assert.False(new NoRateLimitRule().Evaluate(Gateway("{}")).IsApplicable);
    }

    private static ArchitectureGraph ApiAndDb(string configuration) => new GraphBuilder()
        .Node("Api", NodeType.Api, configuration)
        .Node("Db", NodeType.Database)
        .Connect("Api", "Db", protocol: ProtocolType.Sql)
        .Build();

    private static ArchitectureGraph Gateway(string configuration) => new GraphBuilder()
        .Node("Gw", NodeType.Gateway, configuration)
        .Node("Db", NodeType.Database)
        .Connect("Gw", "Db", protocol: ProtocolType.Sql)
        .Build();
}

public class NoObservabilityRuleTests
{
    [Fact]
    public void Flags_an_architecture_with_nothing_watching()
    {
        var finding = Assert.Single(new NoObservabilityRule().Evaluate(Base().Build()).Findings);

        Assert.Contains("No hay nada observando", finding.Message.Es());
    }

    [Fact]
    public void A_connected_observability_component_silences_it()
    {
        // The chain D5.3 had to walk whole: without a component there is no rule, and without a
        // rule the area stays empty. This is the far end of it.
        var graph = Base()
            .Node("Telemetry", NodeType.Observability)
            .Connect("Api", "Telemetry", synchronous: false)
            .Build();

        Assert.Empty(new NoObservabilityRule().Evaluate(graph).Findings);
    }

    [Fact]
    public void An_observability_component_wired_to_nothing_does_not_count()
    {
        var graph = Base().Node("Telemetry", NodeType.Observability).Build();
        var finding = Assert.Single(new NoObservabilityRule().Evaluate(graph).Findings);

        Assert.Contains("no está conectado", finding.Message.Es());
    }

    private static GraphBuilder Base() => new GraphBuilder()
        .Node("Api", NodeType.Api)
        .Node("Db", NodeType.Database)
        .Connect("Api", "Db", protocol: ProtocolType.Sql);
}
