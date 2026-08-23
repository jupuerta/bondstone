using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
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
        Assert.Equal("finding.ORPHAN_NODE.disconnected.message(node=Leftover)", finding.Message.Describe());
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
        Assert.Equal("finding.QUEUE_NO_DLQ.missing.message", Assert.Single(result.Findings).Message.Key());
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

    [Fact]
    public void A_queue_nobody_drains_is_not_asked_for_its_dead_letter_queue_yet()
    {
        // QA §4: a message that is never processed cannot fail processing, so the DLQ has nothing
        // to catch. QUEUE_NO_CONSUMER owns that box until the consumer is drawn - one lesson at a
        // time, under one code (D4.5).
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Q", NodeType.Queue)
            .Connect("Api", "Q", synchronous: false, protocol: ProtocolType.Amqp)
            .Build();

        var result = new QueueNoDlqRule().Evaluate(graph);

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void A_queue_delivering_only_to_a_database_is_not_asked_either()
    {
        // V14 B8. The arrow to the database used to pass for a consumer, so this rule spoke and
        // QUEUE_NO_CONSUMER did not: the second-level advice handed out over the missing first.
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Q", NodeType.Queue)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Q", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Q", "Db", synchronous: false, protocol: ProtocolType.Amqp)
            .Build();

        Assert.Empty(new QueueNoDlqRule().Evaluate(graph).Findings);
        Assert.Single(new QueueNoConsumerRule().Evaluate(graph).Findings);
    }

    // A queue with a producer and a consumer: the flow this rule has something to say about.
    private static ArchitectureGraph Queue(string configuration) => new GraphBuilder()
        .Node("Api", NodeType.Api)
        .Node("Q", NodeType.Queue, configuration)
        .Node("Worker", NodeType.Worker)
        .Connect("Api", "Q", synchronous: false, protocol: ProtocolType.Amqp)
        .Connect("Q", "Worker", synchronous: false, protocol: ProtocolType.Amqp)
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
        Assert.Equal("finding.TIMEOUT_TOO_SHORT.expires.message", finding.Message.Key());
    }

    [Fact]
    public void It_needs_CallCost_which_is_why_V2_could_not_have_it()
    {
        // The debt was not deferred out of laziness: knowing what a call costs is exactly what V3
        // wrote CallCost for. The suggestion quotes that figure back.
        var finding = Assert.Single(new TimeoutTooShortRule().Evaluate(ApiAndDb(1_200)).Findings);

        Assert.Equal("finding.TIMEOUT_TOO_SHORT.expires.suggestion", finding.Suggestion.Key());
    }

    private static ArchitectureGraph ApiAndDb(int rps) => new GraphBuilder()
        .Node("Api", NodeType.Api, rps == 0 ? "{}" : $$"""{"requestsPerSecond":{{rps}}}""")
        .Node("Db", NodeType.Database)
        .Connect("Api", "Db", timeoutSeconds: 1, protocol: ProtocolType.Sql)
        .Build();

    [Fact]
    public void A_providers_own_timeout_is_not_the_architects_knob()
    {
        // V14 B10. Whatever Stripe set on its call to Maps expires or does not on Stripe's side:
        // a one-second timeout on the provider's hop, under a load that would make it expire, is
        // not an edge this rule has anything to say about. Your own call, with a generous
        // timeout, is the one it reads - and finds fine.
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":1200}""")
            .Node("Stripe", NodeType.ExternalService)
            .Node("Maps", NodeType.ExternalService)
            .Connect("Api", "Stripe", timeoutSeconds: 30, fallback: true)
            .Connect("Stripe", "Maps", timeoutSeconds: 1)
            .Build();

        var result = new TimeoutTooShortRule().Evaluate(graph);

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

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

        // The two numbers that let you fix it travel raw; the sentence around them is the client's.
        Assert.Equal("300", finding.Explanation.Arg("loadRps"));
        Assert.Equal("200", finding.Explanation.Arg("capacityRps"));
        Assert.Equal("finding.UNDERSIZED_COMPONENT.suggestion.database", finding.Suggestion.Key());
    }

    [Fact]
    public void The_telemetry_edge_does_not_make_an_overloaded_leaf_a_relay()
    {
        // QA B9: the clause about "what is behind" is for dependencies. An Api whose only outgoing
        // edge is its telemetry has nothing behind it in the sense the lesson means.
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":1000}""")
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Obs", synchronous: false)
            .Build();

        var finding = Assert.Single(new UndersizedComponentRule().Evaluate(graph).Findings);

        Assert.Equal("finding.UNDERSIZED_COMPONENT.overloaded.explanation", finding.Explanation.Key());
    }

    // ---- V14 B2: the exclusion is a predicate, and it names five --------------

    [Theory]
    [InlineData(NodeType.Client, """{"requestsPerSecond":2000000}""")]
    [InlineData(NodeType.Scheduler, """{"jobsPerSecond":5000}""")]
    public void What_is_not_yours_to_size_is_never_told_to_grow(NodeType type, string configuration)
    {
        // T9. "Give your users more instances" and "add a replica of the cron" are the same
        // impossible sentence UNDERSIZED_COMPONENT already refuses to say about a third party. The
        // enormous capacity CapacityModel hands these boxes so they never saturate was no guard
        // against a declared load big enough to get past it.
        var graph = new GraphBuilder()
            .Node("Origin", type, configuration)
            .Node("Worker", NodeType.Worker, """{"instances":1000}""")
            .Connect("Origin", "Worker")
            .Build();

        var origin = graph.Nodes.Single(n => n.Name == "Origin").Id;

        Assert.True(LoadModel.Of(graph).UtilizationOf(origin) > 1m);
        Assert.DoesNotContain(new UndersizedComponentRule().Evaluate(graph).Findings, f => f.Elements.Any(e => e.Id == origin));
    }

    [Fact]
    public void The_cdn_is_a_providers_edge_not_a_box_you_scale()
    {
        // T9's third: a CDN with every request missing, under two million of them. "Spread its
        // work over several components" was the advice - for somebody else's edge.
        var graph = new GraphBuilder()
            .Node("Web", NodeType.Client, """{"requestsPerSecond":2000000}""")
            .Node("Edge", NodeType.Cdn, """{"hitRatio":0}""")
            .Node("Gw", NodeType.Gateway, """{"rateLimit":100}""")
            .Connect("Web", "Edge").Connect("Edge", "Gw")
            .Build();

        var edge = graph.Nodes.Single(n => n.Name == "Edge").Id;

        Assert.True(LoadModel.Of(graph).UtilizationOf(edge) > 1m);
        Assert.DoesNotContain(new UndersizedComponentRule().Evaluate(graph).Findings, f => f.Elements.Any(e => e.Id == edge));
    }

    [Fact]
    public void What_is_yours_behind_them_is_still_sized()
    {
        // The guard: leaving the origin out does not leave out what it floods. The worker behind
        // the cron is yours, and it is drowning.
        var graph = new GraphBuilder()
            .Node("Cron", NodeType.Scheduler, """{"jobsPerSecond":5000}""")
            .Node("Worker", NodeType.Worker)
            .Connect("Cron", "Worker")
            .Build();

        var finding = Assert.Single(new UndersizedComponentRule().Evaluate(graph).Findings);

        Assert.Equal(graph.Nodes.Single(n => n.Name == "Worker").Id, Assert.Single(finding.Elements).Id);
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
    public void An_entry_point_that_declares_no_authentication_is_critical()
    {
        // V13 D2.1. Critical is now reserved for the *declared* no: somebody looked at a door with a
        // database behind it and said it asks nobody anything.
        var finding = Assert.Single(
            new UnauthenticatedEntryRule().Evaluate(ApiAndDb("""{"authentication":false}""")).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.UNAUTHENTICATED_ENTRY.open.message", finding.Message.Key());
        Assert.Equal("Api", finding.Message.Arg("node"));
    }

    [Fact]
    public void An_entry_point_that_never_declared_it_is_only_a_warning()
    {
        // V13 D2.1, and the measurement that forced it: this is `minimal-api-db`, two boxes and an
        // arrow, which used to open with a red critical because an undeclared field and a declared
        // "no" were the same `false`.
        var finding = Assert.Single(new UnauthenticatedEntryRule().Evaluate(ApiAndDb("{}")).Findings);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.UNAUTHENTICATED_ENTRY.undeclared.message", finding.Message.Key());
        Assert.Equal("Api", finding.Message.Arg("node"));
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
        Assert.Equal("finding.EXPOSED_DATABASE.reachable.message", finding.Message.Key());
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

    [Fact]
    public void An_api_that_is_the_door_is_asked_the_same_question()
    {
        // QA §4. Until the field existed, an Api with a thousand declared requests a second was a
        // door this rule could never speak to. The advice is the door's: limit here, or put a
        // gateway in front that does.
        var unlimited = Assert.Single(new NoRateLimitRule().Evaluate(ApiAndDb("""{"requestsPerSecond":1000}""")).Findings);
        Assert.Equal("finding.NO_RATE_LIMIT.unlimited.suggestion.api", unlimited.Suggestion.Key());

        Assert.Empty(new NoRateLimitRule().Evaluate(ApiAndDb("""{"requestsPerSecond":1000,"rateLimit":500}""")).Findings);
    }

    [Fact]
    public void The_volume_is_what_reaches_the_door_not_what_it_declares()
    {
        // The users' traffic lives on the Client since QA §3.1; a gateway with a Client in front
        // receives every one of those requests whether or not it declared any itself.
        var graph = new GraphBuilder()
            .Node("Web", NodeType.Client, """{"requestsPerSecond":1000}""")
            .Node("Gw", NodeType.Gateway)
            .Node("Db", NodeType.Database)
            .Connect("Web", "Gw")
            .Connect("Gw", "Db", protocol: ProtocolType.Sql)
            .Build();

        var finding = Assert.Single(new NoRateLimitRule().Evaluate(graph).Findings);
        Assert.Equal("Gw", finding.Message.Arg("node"));
    }

    // ---- V14 B6: a root is a door only if the outside could reach it ----------

    [Theory]
    [InlineData(NodeType.Queue)]
    [InlineData(NodeType.Cache)]
    [InlineData(NodeType.Service)]
    [InlineData(NodeType.Database)]
    public void A_root_the_outside_could_not_reach_is_not_the_internet(NodeType type)
    {
        // T2a, T2b, U2. Drawing only part of a system puts a queue, a cache or a service at the
        // top of the flow, and "every root is a door except three types" read each of them as the
        // internet - so the data behind it was a breach. A store or a broker at the root is a
        // diagram that starts in the middle.
        var graph = new GraphBuilder()
            .Node("Root", type)
            .Node("Db", NodeType.Database)
            .Connect("Root", "Db", protocol: ProtocolType.Sql)
            .Build();

        var result = new ExposedDatabaseRule().Evaluate(graph);

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData(NodeType.LoadBalancer)]
    [InlineData(NodeType.Cdn)]
    [InlineData(NodeType.ExternalService)]
    public void A_root_the_outside_reaches_is_still_a_door(NodeType type)
    {
        // The guard: the edge, the balancer and a third party calling in (a webhook is outside
        // traffic) keep inferring "internet" without a Client drawn.
        var graph = new GraphBuilder()
            .Node("Root", type)
            .Node("Db", NodeType.Database)
            .Connect("Root", "Db", protocol: ProtocolType.Sql)
            .Build();

        Assert.Single(new ExposedDatabaseRule().Evaluate(graph).Findings);
    }

    [Fact]
    public void Two_services_on_their_own_databases_are_not_two_breaches()
    {
        // The scenario V13 documented its B2 with, rerun in the V14 sweep: the two SPOF Warnings
        // it promised were there, and beside them two Critical EXPOSED_DATABASE nobody had
        // promised, because the two services were roots. Security 78 on the canonical
        // microservices drawing with no gateway drawn.
        var graph = new GraphBuilder()
            .Nodes(NodeType.Service, "Users", "Orders")
            .Node("users-db", NodeType.Database).Node("orders-db", NodeType.Database)
            .Connect("Users", "users-db", protocol: ProtocolType.Sql)
            .Connect("Orders", "orders-db", protocol: ProtocolType.Sql)
            .Build();

        Assert.Empty(new ExposedDatabaseRule().Evaluate(graph).Findings);
    }

    [Fact]
    public void The_scrape_does_not_take_the_door_out_of_Securitys_sight()
    {
        // The B1 bug, one area over, and one the sweep did not catch: `Obs -> Api` took the Api
        // out of RootNodes, the old inference found no origin at all, and every door rule went
        // quiet - EXPOSED_DATABASE green, the other two not applicable - the moment the telemetry
        // was wired the way NO_OBSERVABILITY asks. Origins are read over TrafficRoots now.
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":100}""")
            .Node("Db", NodeType.Database)
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Db", protocol: ProtocolType.Sql)
            .Connect("Obs", "Api", synchronous: false)
            .Connect("Obs", "Db", synchronous: false)
            .Build();

        Assert.Single(new UnauthenticatedEntryRule().Evaluate(graph).Findings);
        Assert.Single(new NoRateLimitRule().Evaluate(graph).Findings);
    }

    // ---- V14 B6 (N12): a Service facing the outside is a door ------------------

    [Fact]
    public void A_bff_is_a_door_and_gets_the_doors_question_not_a_shape_error()
    {
        // `Client -> Bff(Service) -> Db`, an ordinary BFF. The frontier walked through the
        // service and called the database exposed, with advice to put "an API or a gateway" in
        // front - that is, to rename the box. The right finding is the one every door gets.
        var graph = new GraphBuilder()
            .Node("Web", NodeType.Client)
            .Node("Bff", NodeType.Service)
            .Node("Db", NodeType.Database)
            .Connect("Web", "Bff")
            .Connect("Bff", "Db", protocol: ProtocolType.Sql)
            .Build();

        Assert.Empty(new ExposedDatabaseRule().Evaluate(graph).Findings);

        var finding = Assert.Single(new UnauthenticatedEntryRule().Evaluate(graph).Findings);
        Assert.Equal("finding.UNAUTHENTICATED_ENTRY.undeclared.message", finding.Message.Key());
        Assert.Equal("Bff", finding.Message.Arg("node"));
    }

    [Fact]
    public void A_bff_can_answer_the_question()
    {
        // The field that closes the loop, on the type that did not have it: declared "yes"
        // clears it, declared "no" is the Critical it has always been on an Api.
        Assert.Empty(new UnauthenticatedEntryRule().Evaluate(Bff("""{"authentication":true}""")).Findings);

        Assert.Equal(
            Severity.Critical,
            Assert.Single(new UnauthenticatedEntryRule().Evaluate(Bff("""{"authentication":false}""")).Findings).Severity);
    }

    [Fact]
    public void A_bff_cannot_limit_so_its_advice_is_the_gateway()
    {
        // NO_RATE_LIMIT asks every door. A Service has no limit to set, so the generic sentence
        // would point at a field it does not have - the B4 mistake - and the advice is the only
        // move it has (V13 D1.2).
        var finding = Assert.Single(new NoRateLimitRule().Evaluate(Bff("{}", rps: 100)).Findings);

        Assert.Equal("finding.NO_RATE_LIMIT.unlimited.suggestion.service", finding.Suggestion.Key());
    }

    // ---- V14 B15: two declarations that contradict each other --------------------

    [Fact]
    public void A_door_that_declares_no_and_wires_an_identity_provider_is_told_to_decide()
    {
        // QA V14 N15. "The drawing is the stronger claim" settles silence against a wire; it does
        // not settle a declared "no" against a wire, and choosing one quietly was the analyzer
        // making the user's decision. A Warning naming both, pointing at the door and the wire.
        var builder = Delegating("""{"authentication":false}""");

        var finding = Assert.Single(new UnauthenticatedEntryRule().Evaluate(builder.Build()).Findings);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.UNAUTHENTICATED_ENTRY.contradicted.message", finding.Message.Key());
        Assert.Equal("Api", finding.Message.Arg("node"));
        Assert.Equal("IdP", finding.Message.Arg("provider"));
        Assert.Equal("finding.UNAUTHENTICATED_ENTRY.contradicted.explanation", finding.Explanation.Key());
        Assert.Equal("finding.UNAUTHENTICATED_ENTRY.contradicted.suggestion", finding.Suggestion.Key());
        Assert.Equal(["Api", "Api->IdP"], builder.Names(finding.Elements));
    }

    [Fact]
    public void A_declared_yes_and_a_wired_identity_provider_agree()
    {
        Assert.Empty(new UnauthenticatedEntryRule().Evaluate(Delegating("""{"authentication":true}""").Build()).Findings);
    }

    [Fact]
    public void An_undeclared_door_with_a_wired_identity_provider_is_still_the_delegation()
    {
        // QA §3.4 unchanged: silence plus a wire is the wire.
        Assert.Empty(new UnauthenticatedEntryRule().Evaluate(Delegating("{}").Build()).Findings);
    }

    private static GraphBuilder Delegating(string configuration) => new GraphBuilder()
        .Node("Api", NodeType.Api, configuration)
        .Node("IdP", NodeType.IdentityProvider)
        .Node("Db", NodeType.Database)
        .Connect("Api", "IdP", timeoutSeconds: 1)
        .Connect("Api", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

    private static ArchitectureGraph Bff(string configuration, int rps = 0) => new GraphBuilder()
        .Node("Web", NodeType.Client, rps == 0 ? "{}" : $$"""{"requestsPerSecond":{{rps}}}""")
        .Node("Bff", NodeType.Service, configuration)
        .Node("Db", NodeType.Database)
        .Connect("Web", "Bff")
        .Connect("Bff", "Db", protocol: ProtocolType.Sql)
        .Build();

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
    public void Every_component_nobody_watches_is_a_finding()
    {
        // QA §4, coverage. Nothing watching used to be one Warning; it is now one per component
        // that is yours to watch, which is what lets the area climb as the wiring does.
        var findings = new NoObservabilityRule().Evaluate(Base().Build()).Findings;

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal("finding.NO_OBSERVABILITY.unobserved.message", f.Message.Key()));
        Assert.Equal(["Api", "Db"], findings.Select(f => f.Message.Arg("node")));

        // With no telemetry on the canvas the advice starts with adding it, and there is nothing
        // to offer a button for.
        Assert.All(findings, f => Assert.Equal("finding.NO_OBSERVABILITY.unobserved.suggestion.absent", f.Suggestion.Key()));
        Assert.All(findings, f => Assert.Null(f.Fix));
    }

    [Fact]
    public void Wiring_one_component_clears_that_component_and_no_other()
    {
        // The chain D5.3 had to walk whole: without a component there is no rule, and without a
        // rule the area stays empty. This is the far end of it - and the grain QA §4 gave it: the
        // Api is watched, the database is not, and the card says which.
        var graph = Base()
            .Node("Telemetry", NodeType.Observability)
            .Connect("Api", "Telemetry", synchronous: false)
            .Build();

        var finding = Assert.Single(new NoObservabilityRule().Evaluate(graph).Findings);

        Assert.Equal("Db", finding.Message.Arg("node"));
        Assert.Equal("finding.NO_OBSERVABILITY.unobserved.suggestion.present(node=Db, telemetry=Telemetry)", finding.Suggestion.Describe());

        // D5.6: with exactly one telemetry box there is nothing to decide, so the card has a button.
        Assert.NotNull(finding.Fix);
        Assert.Equal(FixKinds.ConnectTo, finding.Fix!.Kind);
        Assert.Equal(graph.NodeId("Db"), finding.Fix.Element.Id);
        Assert.Equal(graph.NodeId("Telemetry"), finding.Fix.TargetNodeId);
    }

    [Fact]
    public void Either_direction_counts_as_watching()
    {
        // Telemetry scraping the database and the Api pushing to the telemetry are both wiring.
        var graph = Base()
            .Node("Telemetry", NodeType.Observability)
            .Connect("Api", "Telemetry", synchronous: false)
            .Connect("Telemetry", "Db", synchronous: false)
            .Build();

        var result = new NoObservabilityRule().Evaluate(graph);

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void An_observability_component_wired_to_nothing_watches_nothing()
    {
        var graph = Base().Node("Telemetry", NodeType.Observability).Build();
        var findings = new NoObservabilityRule().Evaluate(graph).Findings;

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal("finding.NO_OBSERVABILITY.unobserved.suggestion.present", f.Suggestion.Key()));
    }

    [Fact]
    public void With_two_telemetry_boxes_which_one_is_the_decision()
    {
        var graph = Base()
            .Node("Metrics", NodeType.Observability)
            .Node("Logs", NodeType.Observability)
            .Connect("Api", "Metrics", synchronous: false)
            .Build();

        var finding = Assert.Single(new NoObservabilityRule().Evaluate(graph).Findings);

        Assert.Equal("finding.NO_OBSERVABILITY.unobserved.suggestion.several", finding.Suggestion.Key());
        Assert.Null(finding.Fix);
    }

    [Fact]
    public void What_is_not_yours_to_instrument_is_not_asked_for()
    {
        // The outside, a third party and the CDN: the same three the analyzer declines to size or
        // to replicate, for the same reason. The orphan box is ORPHAN_NODE's.
        var graph = new GraphBuilder()
            .Node("Web", NodeType.Client)
            .Node("Edge", NodeType.Cdn)
            .Node("Api", NodeType.Api)
            .Node("Payments", NodeType.ExternalService)
            .Node("Telemetry", NodeType.Observability)
            .Node("Leftover", NodeType.Service)
            .Connect("Web", "Edge")
            .Connect("Edge", "Api")
            .Connect("Api", "Payments", timeoutSeconds: 5, fallback: true)
            .Connect("Api", "Telemetry", synchronous: false)
            .Build();

        var result = new NoObservabilityRule().Evaluate(graph);

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Wiring_the_telemetry_costs_nothing_in_the_structural_rules()
    {
        // The rule asks for one wire per component; HIGH_COUPLING, MISSING_CACHE and
        // QUEUE_NO_CONSUMER must not charge for any of them - or for the telemetry's own fan-out.
        // Three dependencies on the Api - two services and the queue - one under the bar of four.
        var bare = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Nodes(NodeType.Service, "A", "B")
            .Node("Db", NodeType.Database)
            .Node("Cola", NodeType.Queue)
            .Connect("Api", "A", timeoutSeconds: 3).Connect("Api", "B", timeoutSeconds: 3)
            .Connect("A", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Api", "Cola", synchronous: false, protocol: ProtocolType.Amqp);

        var wired = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Nodes(NodeType.Service, "A", "B")
            .Node("Db", NodeType.Database)
            .Node("Cola", NodeType.Queue)
            .Node("Telemetry", NodeType.Observability)
            .Connect("Api", "A", timeoutSeconds: 3).Connect("Api", "B", timeoutSeconds: 3)
            .Connect("A", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Api", "Cola", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Api", "Telemetry", synchronous: false)
            .Connect("Telemetry", "A").Connect("Telemetry", "B")
            .Connect("Telemetry", "Db")
            .Connect("Cola", "Telemetry", synchronous: false);

        // The Api has three dependencies in both drawings: the fourth edge is instrumentation,
        // and the telemetry's own four edges are its job.
        Assert.Empty(new HighCouplingRule().Evaluate(bare.Build()).Findings);
        Assert.Empty(new HighCouplingRule().Evaluate(wired.Build()).Findings);

        // The telemetry reading the database is not a reader that wants a cache.
        Assert.Empty(new MissingCacheRule().Evaluate(wired.Build()).Findings);

        // And the telemetry watching the queue is not draining it.
        Assert.Single(new QueueNoConsumerRule().Evaluate(bare.Build()).Findings);
        Assert.Single(new QueueNoConsumerRule().Evaluate(wired.Build()).Findings);
    }

    private static GraphBuilder Base() => new GraphBuilder()
        .Node("Api", NodeType.Api)
        .Node("Db", NodeType.Database)
        .Connect("Api", "Db", protocol: ProtocolType.Sql);
}
