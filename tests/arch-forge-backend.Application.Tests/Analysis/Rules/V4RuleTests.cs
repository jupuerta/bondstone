using bondstone_backend.Application.Tests.TestDoubles;
using bondstone_backend.Application.Localization;
using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Analysis.Rules;
using bondstone_backend.Application.Simulation;
using bondstone_backend.Application.Tests.Analysis.Fixtures;
using bondstone_backend.Application.Tests.Analysis.Rules.Support;
using bondstone_backend.Domain.Enums;
using Xunit;

namespace bondstone_backend.Application.Tests.Analysis.Rules;

public class OrphanNodeRuleTests
{
    [Fact]
    public void Flags_a_component_nobody_calls_and_that_calls_nobody()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Node("Leftover", NodeType.Service)
            .Connect("Api", "Db")
            .Build();

        var result = new OrphanNodeRule().Evaluate(graph);

        Assert.True(result.IsApplicable);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("finding.ORPHAN_NODE.disconnected.message(node=Leftover)", finding.Message.Describe());
    }

    [Fact]
    public void A_single_component_is_unfinished_rather_than_wrong()
    {
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
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Q", NodeType.Queue)
            .Connect("Api", "Q", synchronous: false)
            .Build();

        var result = new QueueNoDlqRule().Evaluate(graph);

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void A_queue_delivering_only_to_a_database_is_not_asked_either()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Q", NodeType.Queue)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Q", synchronous: false)
            .Connect("Q", "Db", synchronous: false)
            .Build();

        Assert.Empty(new QueueNoDlqRule().Evaluate(graph).Findings);
        Assert.Single(new QueueNoConsumerRule().Evaluate(graph).Findings);
    }

    private static ArchitectureGraph Queue(string configuration) => new GraphBuilder()
        .Node("Api", NodeType.Api)
        .Node("Q", NodeType.Queue, configuration)
        .Node("Worker", NodeType.Worker)
        .Connect("Api", "Q", synchronous: false)
        .Connect("Q", "Worker", synchronous: false)
        .Build();
}

public class TimeoutTooShortRuleTests
{
    [Fact]
    public void Traffic_is_what_makes_a_timeout_too_short()
    {
        var idle = ApiAndDb(0);
        var loaded = ApiAndDb(1_200);

        Assert.Empty(new TimeoutTooShortRule().Evaluate(idle).Findings);

        var finding = Assert.Single(new TimeoutTooShortRule().Evaluate(loaded).Findings);
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.TIMEOUT_TOO_SHORT.expires.message", finding.Message.Key());
    }

    // QA 30-08 §10.3, point 12. What makes the call slower than its own timeout is the queue at the
    // destination, so raising the number buys a slower answer — which is what `timeout-was-enough`
    // teaches not to do. With the destination full the sentence starts with capacity and there is no
    // button; with the destination calm and a slow chain behind it, raising it is the right move.
    [Fact]
    public void It_asks_for_capacity_and_no_longer_offers_to_raise_the_number()
    {
        var finding = Assert.Single(new TimeoutTooShortRule().Evaluate(ApiAndDb(1_200)).Findings);

        Assert.Equal("finding.TIMEOUT_TOO_SHORT.expires.suggestion", finding.Suggestion.Key());
        Assert.Equal("Db", finding.Suggestion.Arg("target"));
        Assert.Null(finding.Fix);
    }

    private static ArchitectureGraph ApiAndDb(int rps) => new GraphBuilder()
        .Node("Api", NodeType.Api, rps == 0 ? "{}" : $$"""{"requestsPerSecond":{{rps}}}""")
        .Node("Db", NodeType.Database)
        .Connect("Api", "Db", timeoutSeconds: 1)
        .Build();


    [Fact]
    public void A_target_marked_as_overloaded_is_not_this_rules_finding()
    {
        var graph = ApiAndDb(1_200);
        graph.Facts.Add(new UndersizedComponentFact(graph.Nodes.Single(n => n.Name == "Db").Id, Severity.Critical));

        var result = new TimeoutTooShortRule().Evaluate(graph);

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void A_target_merely_close_to_its_limit_still_gets_its_timeout_read()
    {
        var graph = NearCapacityChain();
        graph.Facts.Add(new UndersizedComponentFact(graph.Nodes.Single(n => n.Name == "Svc").Id, Severity.Warning));

        var finding = Assert.Single(new TimeoutTooShortRule().Evaluate(graph).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("Svc", finding.Message.Arg("target"));
    }

    [Fact]
    public void What_the_call_waits_on_counts_as_much_as_where_it_lands()
    {
        var graph = GatewayApiAndDrowningDb();

        Assert.Single(new TimeoutTooShortRule().Evaluate(graph).Findings);

        graph.Facts.Add(new UndersizedComponentFact(graph.Nodes.Single(n => n.Name == "Db").Id, Severity.Critical));

        Assert.Empty(new TimeoutTooShortRule().Evaluate(graph).Findings);
    }

    private static ArchitectureGraph NearCapacityChain() => new GraphBuilder()
        .Node("Api", NodeType.Api, """{"requestsPerSecond":285}""")
        .Node("Svc", NodeType.Service)
        .Node("Stripe", NodeType.ExternalService)
        .Connect("Api", "Svc", timeoutSeconds: 1)
        .Connect("Svc", "Stripe", timeoutSeconds: 5, fallback: true)
        .Build();

    private static ArchitectureGraph GatewayApiAndDrowningDb() => new GraphBuilder()
        .Node("Gw", NodeType.Gateway, """{"requestsPerSecond":1000}""")
        .Node("Api", NodeType.Api, """{"instances":4}""")
        .Node("Db", NodeType.Database)
        .Connect("Gw", "Api", timeoutSeconds: 1)
        .Connect("Api", "Db")
        .Build();

    [Fact]
    public void A_providers_own_timeout_is_not_the_architects_knob()
    {
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
            .Connect("Api", "Db")
            .Build();

        Assert.False(new TimeoutTooShortRule().Evaluate(graph).IsApplicable);
    }
}

public class UndersizedComponentRuleTests
{
    [Fact]
    public void Says_nothing_until_somebody_declares_how_much_traffic_comes_in()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db")
            .Build();

        Assert.False(new UndersizedComponentRule().Evaluate(graph).IsApplicable);
    }

    [Fact]
    public void Over_capacity_is_critical_and_close_to_it_is_a_warning()
    {
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

        Assert.Equal("300", finding.Explanation.Arg("loadRps"));
        Assert.Equal("200", finding.Explanation.Arg("capacityRps"));
        Assert.Equal("finding.UNDERSIZED_COMPONENT.suggestion.database", finding.Suggestion.Key());
    }

    [Fact]
    public void The_telemetry_edge_does_not_make_an_overloaded_leaf_a_relay()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":1000}""")
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Obs", synchronous: false)
            .Build();

        var finding = Assert.Single(new UndersizedComponentRule().Evaluate(graph).Findings);

        Assert.Equal("finding.UNDERSIZED_COMPONENT.overloaded.explanation", finding.Explanation.Key());
    }


    [Theory]
    [InlineData(NodeType.Client, """{"requestsPerSecond":2000000}""")]
    [InlineData(NodeType.Scheduler, """{"jobsPerSecond":5000}""")]
    public void What_is_not_yours_to_size_is_never_told_to_grow(NodeType type, string configuration)
    {
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
        .Connect("Api", "Db")
        .Build();
}

public class SecurityRuleTests
{
    [Fact]
    public void An_entry_point_that_declares_no_authentication_is_critical()
    {
        var finding = Assert.Single(
            new UnauthenticatedEntryRule().Evaluate(ApiAndDb("""{"requestsPerSecond":100,"authentication":false}""")).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.UNAUTHENTICATED_ENTRY.open.message", finding.Message.Key());
        Assert.Equal("Api", finding.Message.Arg("node"));
    }

    // QA 30-08 §4.3: declaring the door open cost a critical and saying nothing a warning, so the
    // honest drawing scored worse than the silent one. A door nobody has vouched for is open until
    // proven otherwise — same weight, its own sentence.
    [Fact]
    public void An_entry_point_that_never_declared_it_is_open_until_proven_otherwise()
    {
        var finding = Assert.Single(new UnauthenticatedEntryRule().Evaluate(ApiAndDb("""{"requestsPerSecond":100}""")).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.UNAUTHENTICATED_ENTRY.undeclared.message", finding.Message.Key());
        Assert.Equal("Api", finding.Message.Arg("node"));
    }

    // V13 D2.1 kept two boxes and an arrow out of the red, and 7.3.6 put them back by making the
    // undeclared door a critical (QA 30-08 §10.2, point 1). Both hold: a door nobody calls — no
    // traffic reaching it, no client drawn — is not asked, whatever it declares, so saying «no»
    // costs exactly what saying nothing costs. NoRateLimitRule has stood down the same way since V4.
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"authentication":false}""")]
    public void Two_boxes_and_an_arrow_are_not_an_open_door(string configuration)
    {
        var result = new UnauthenticatedEntryRule().Evaluate(ApiAndDb(configuration));

        Assert.False(result.IsApplicable);
        Assert.Equal("rule.na.UNAUTHENTICATED_ENTRY.noTrafficAtDoor", result.NotApplicableReason!.Key());
    }

    [Fact]
    public void A_drawn_client_is_the_traffic_number_or_not()
    {
        var graph = new GraphBuilder()
            .Node("Users", NodeType.Client)
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Users", "Api")
            .Connect("Api", "Db")
            .Build();

        var finding = Assert.Single(new UnauthenticatedEntryRule().Evaluate(graph).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.UNAUTHENTICATED_ENTRY.undeclared.message", finding.Message.Key());
    }

    [Fact]
    public void Traffic_declared_upstream_reaches_the_door()
    {
        var graph = new GraphBuilder()
            .Node("LB", NodeType.LoadBalancer, """{"requestsPerSecond":100}""")
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("LB", "Api")
            .Connect("Api", "Db")
            .Build();

        var finding = Assert.Single(new UnauthenticatedEntryRule().Evaluate(graph).Findings);

        Assert.Equal("Api", finding.Message.Arg("node"));
    }

    [Fact]
    public void Authentication_silences_it()
    {
        var result = new UnauthenticatedEntryRule().Evaluate(ApiAndDb("""{"authentication":true}"""));

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void With_no_data_behind_the_door_there_is_nothing_to_protect()
    {
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
        var graph = new GraphBuilder()
            .Node("LB", NodeType.LoadBalancer, """{"requestsPerSecond":100}""")
            .Node("Api1", NodeType.Api)
            .Node("Api2", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("LB", "Api1")
            .Connect("LB", "Api2")
            .Connect("Api1", "Db")
            .Connect("Api2", "Db")
            .Build();

        Assert.Equal(2, new UnauthenticatedEntryRule().Evaluate(graph).Findings.Count);
    }

    [Fact]
    public void A_database_reachable_without_passing_through_an_api_is_exposed()
    {
        var graph = new GraphBuilder()
            .Node("LB", NodeType.LoadBalancer)
            .Node("Db", NodeType.Database)
            .Connect("LB", "Db")
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
        var unlimited = Assert.Single(new NoRateLimitRule().Evaluate(ApiAndDb("""{"requestsPerSecond":1000}""")).Findings);
        Assert.Equal("finding.NO_RATE_LIMIT.unlimited.suggestion.api", unlimited.Suggestion.Key());

        Assert.Empty(new NoRateLimitRule().Evaluate(ApiAndDb("""{"requestsPerSecond":1000,"rateLimit":500}""")).Findings);
    }

    [Fact]
    public void The_volume_is_what_reaches_the_door_not_what_it_declares()
    {
        var graph = new GraphBuilder()
            .Node("Web", NodeType.Client, """{"requestsPerSecond":1000}""")
            .Node("Gw", NodeType.Gateway)
            .Node("Db", NodeType.Database)
            .Connect("Web", "Gw")
            .Connect("Gw", "Db")
            .Build();

        var finding = Assert.Single(new NoRateLimitRule().Evaluate(graph).Findings);
        Assert.Equal("Gw", finding.Message.Arg("node"));
    }


    [Theory]
    [InlineData(NodeType.Queue)]
    [InlineData(NodeType.Cache)]
    [InlineData(NodeType.Service)]
    [InlineData(NodeType.Database)]
    public void A_root_the_outside_could_not_reach_is_not_the_internet(NodeType type)
    {
        var graph = new GraphBuilder()
            .Node("Root", type)
            .Node("Db", NodeType.Database)
            .Connect("Root", "Db")
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
        var graph = new GraphBuilder()
            .Node("Root", type)
            .Node("Db", NodeType.Database)
            .Connect("Root", "Db")
            .Build();

        Assert.Single(new ExposedDatabaseRule().Evaluate(graph).Findings);
    }

    [Fact]
    public void Two_services_on_their_own_databases_are_not_two_breaches()
    {
        var graph = new GraphBuilder()
            .Nodes(NodeType.Service, "Users", "Orders")
            .Node("users-db", NodeType.Database).Node("orders-db", NodeType.Database)
            .Connect("Users", "users-db")
            .Connect("Orders", "orders-db")
            .Build();

        Assert.Empty(new ExposedDatabaseRule().Evaluate(graph).Findings);
    }

    [Fact]
    public void The_scrape_does_not_take_the_door_out_of_Securitys_sight()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":100}""")
            .Node("Db", NodeType.Database)
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Db")
            .Connect("Obs", "Api", synchronous: false)
            .Connect("Obs", "Db", synchronous: false)
            .Build();

        Assert.Single(new UnauthenticatedEntryRule().Evaluate(graph).Findings);
        Assert.Single(new NoRateLimitRule().Evaluate(graph).Findings);
    }


    [Fact]
    public void A_bff_is_a_door_and_gets_the_doors_question_not_a_shape_error()
    {
        var graph = new GraphBuilder()
            .Node("Web", NodeType.Client)
            .Node("Bff", NodeType.Service)
            .Node("Db", NodeType.Database)
            .Connect("Web", "Bff")
            .Connect("Bff", "Db")
            .Build();

        Assert.Empty(new ExposedDatabaseRule().Evaluate(graph).Findings);

        var finding = Assert.Single(new UnauthenticatedEntryRule().Evaluate(graph).Findings);
        Assert.Equal("finding.UNAUTHENTICATED_ENTRY.undeclared.message", finding.Message.Key());
        Assert.Equal("Bff", finding.Message.Arg("node"));
    }

    [Fact]
    public void A_bff_can_answer_the_question()
    {
        Assert.Empty(new UnauthenticatedEntryRule().Evaluate(Bff("""{"authentication":true}""")).Findings);

        Assert.Equal(
            Severity.Critical,
            Assert.Single(new UnauthenticatedEntryRule().Evaluate(Bff("""{"authentication":false}""")).Findings).Severity);
    }

    [Fact]
    public void A_bff_cannot_limit_so_its_advice_is_the_gateway()
    {
        var finding = Assert.Single(new NoRateLimitRule().Evaluate(Bff("{}", rps: 100)).Findings);

        Assert.Equal("finding.NO_RATE_LIMIT.unlimited.suggestion.service", finding.Suggestion.Key());
    }


    [Fact]
    public void A_door_that_declares_no_and_wires_an_identity_provider_is_told_to_decide()
    {
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
        Assert.Empty(new UnauthenticatedEntryRule().Evaluate(Delegating("{}").Build()).Findings);
    }


    [Fact]
    public void The_bucket_behind_a_cdn_is_the_bucket_it_serves()
    {
        var result = new ExposedDatabaseRule().Evaluate(StaticSite().Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void The_two_rules_agree_on_the_static_site()
    {
        var result = ReferenceArchitecture.Analyze(StaticSite().Build());

        Assert.Equal(AreaState.Scored, result.StateOf(Area.Security));
        Assert.Equal(100, result.ScoreOf(Area.Security));
        Assert.DoesNotContain(result.Findings, f => f.Code == RuleCodes.ExposedDatabase && f.Severity != Severity.Good);
        Assert.DoesNotContain(result.Findings, f => f.Code == RuleCodes.ImplausibleDependency && f.Severity != Severity.Good);
    }

    [Theory]
    [InlineData(NodeType.Client)]
    [InlineData(NodeType.LoadBalancer)]
    public void The_public_bucket_of_V13_is_still_the_public_bucket(NodeType inFront)
    {
        var graph = new GraphBuilder()
            .Node("Front", inFront)
            .Node("Assets", NodeType.ObjectStorage)
            .Connect("Front", "Assets")
            .Build();

        var finding = Assert.Single(new ExposedDatabaseRule().Evaluate(graph).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("Assets", finding.Message.Arg("node"));
    }

    [Fact]
    public void A_direct_arrow_beside_the_cdn_is_still_the_open_bucket()
    {
        var graph = StaticSite().Connect("Users", "Assets").Build();

        Assert.Single(new ExposedDatabaseRule().Evaluate(graph).Findings);
    }

    [Fact]
    public void The_cdn_serves_files_not_rows()
    {
        var graph = new GraphBuilder()
            .Node("Users", NodeType.Client)
            .Node("Edge", NodeType.Cdn)
            .Node("Db", NodeType.Database)
            .Connect("Users", "Edge")
            .Connect("Edge", "Db")
            .Build();

        Assert.Single(new ExposedDatabaseRule().Evaluate(graph).Findings);
    }

    [Fact]
    public void The_served_bucket_can_have_a_door_that_writes_to_it()
    {
        var graph = StaticSite()
            .Node("Api", NodeType.Api, """{"authentication":true}""")
            .Connect("Users", "Api")
            .Connect("Api", "Assets")
            .Build();

        Assert.Empty(new ExposedDatabaseRule().Evaluate(graph).Findings);
    }

    [Fact]
    public void Without_a_client_the_cdn_at_the_root_is_still_the_edge()
    {
        var graph = new GraphBuilder()
            .Node("Edge", NodeType.Cdn)
            .Node("Assets", NodeType.ObjectStorage)
            .Connect("Edge", "Assets")
            .Build();

        var result = new ExposedDatabaseRule().Evaluate(graph);

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    private static GraphBuilder StaticSite() => new GraphBuilder()
        .Node("Users", NodeType.Client)
        .Node("Edge", NodeType.Cdn)
        .Node("Assets", NodeType.ObjectStorage)
        .Connect("Users", "Edge")
        .Connect("Edge", "Assets");


    [Fact]
    public void Outside_traffic_that_finds_no_door_is_a_finding_not_a_missing_score()
    {
        var graph = new GraphBuilder()
            .Node("Users", NodeType.Client)
            .Node("Edge", NodeType.Cdn)
            .Node("Worker", NodeType.Worker)
            .Connect("Users", "Edge")
            .Connect("Edge", "Worker")
            .Build();

        var finding = Assert.Single(new UnauthenticatedEntryRule().Evaluate(graph).Findings);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.UNAUTHENTICATED_ENTRY.unguarded.message", finding.Message.Key());
        Assert.Equal("Worker", finding.Message.Arg("node"));
        Assert.Equal("Worker", finding.Explanation.Arg("node"));
        Assert.Equal("Worker", finding.Suggestion.Arg("node"));

        var result = ReferenceArchitecture.Analyze(graph);

        Assert.Equal(AreaState.Scored, result.StateOf(Area.Security));
        Assert.Equal(95, result.ScoreOf(Area.Security));
    }

    [Theory]
    [InlineData(NodeType.Worker)]
    [InlineData(NodeType.Cache)]
    [InlineData(NodeType.Queue)]
    [InlineData(NodeType.Topic)]
    public void What_takes_traffic_and_cannot_ask_must_sit_behind_a_door(NodeType type)
    {
        var finding = Assert.Single(new UnauthenticatedEntryRule().Evaluate(Bare(type)).Findings);

        Assert.Equal("finding.UNAUTHENTICATED_ENTRY.unguarded.message", finding.Message.Key());
        Assert.Equal("Box", finding.Message.Arg("node"));
    }

    [Theory]
    [InlineData(NodeType.ExternalService)]
    [InlineData(NodeType.IdentityProvider)]
    [InlineData(NodeType.Scheduler)]
    [InlineData(NodeType.Observability)]
    public void What_is_not_yours_or_takes_no_traffic_is_not_asked(NodeType type)
    {
        Assert.False(new UnauthenticatedEntryRule().Evaluate(Bare(type)).IsApplicable);
    }

    [Theory]
    [InlineData(NodeType.Database)]
    [InlineData(NodeType.ObjectStorage)]
    public void The_data_reached_bare_is_EXPOSED_DATABASEs_sentence_not_this_ones(NodeType store)
    {
        Assert.False(new UnauthenticatedEntryRule().Evaluate(Bare(store)).IsApplicable);
        Assert.Single(new ExposedDatabaseRule().Evaluate(Bare(store)).Findings);
    }

    [Fact]
    public void The_way_in_is_not_asked_what_hangs_behind_it_is()
    {
        var builder = new GraphBuilder()
            .Node("Users", NodeType.Client)
            .Node("LB", NodeType.LoadBalancer)
            .Node("Worker", NodeType.Worker)
            .Connect("Users", "LB")
            .Connect("LB", "Worker");

        var finding = Assert.Single(new UnauthenticatedEntryRule().Evaluate(builder.Build()).Findings);

        Assert.Equal(["Worker"], builder.Names(finding.Elements));
    }

    [Theory]
    [InlineData(NodeType.Api)]
    [InlineData(NodeType.Gateway)]
    [InlineData(NodeType.Service)]
    public void Behind_a_door_the_same_box_is_where_it_lives(NodeType door)
    {
        var graph = new GraphBuilder()
            .Node("Users", NodeType.Client)
            .Node("Door", door, """{"authentication":true}""")
            .Node("Worker", NodeType.Worker)
            .Connect("Users", "Door")
            .Connect("Door", "Worker")
            .Build();

        Assert.False(new UnauthenticatedEntryRule().Evaluate(graph).IsApplicable);
    }

    [Fact]
    public void A_webhook_into_a_worker_is_outside_traffic_too()
    {
        var graph = new GraphBuilder()
            .Node("Stripe", NodeType.ExternalService)
            .Node("Worker", NodeType.Worker)
            .Connect("Stripe", "Worker")
            .Build();

        var finding = Assert.Single(new UnauthenticatedEntryRule().Evaluate(graph).Findings);

        Assert.Equal("Worker", finding.Message.Arg("node"));
    }

    [Fact]
    public void With_data_behind_the_bare_worker_each_lesson_is_said_once()
    {
        var graph = new GraphBuilder()
            .Node("Users", NodeType.Client)
            .Node("Worker", NodeType.Worker)
            .Node("Db", NodeType.Database)
            .Connect("Users", "Worker")
            .Connect("Worker", "Db")
            .Build();

        var result = ReferenceArchitecture.Analyze(graph);

        Assert.Equal(80, result.ScoreOf(Area.Security));
        Assert.Single(result.Findings, f => f.Code == RuleCodes.ExposedDatabase && f.Severity == Severity.Critical);
        Assert.Single(result.Findings, f => f.Code == RuleCodes.UnauthenticatedEntry && f.Severity == Severity.Warning);
    }

    [Fact]
    public void The_walk_does_not_enter_through_the_instrumentation()
    {
        var graph = new GraphBuilder()
            .Node("Users", NodeType.Client)
            .Node("Edge", NodeType.Cdn)
            .Node("Api", NodeType.Api, """{"authentication":true}""")
            .Node("Worker", NodeType.Worker)
            .Node("Obs", NodeType.Observability)
            .Connect("Users", "Edge")
            .Connect("Edge", "Api")
            .Connect("Api", "Worker")
            .Connect("Edge", "Obs", synchronous: false)
            .Connect("Obs", "Worker", synchronous: false)
            .Build();

        Assert.False(new UnauthenticatedEntryRule().Evaluate(graph).IsApplicable);
    }

    private static ArchitectureGraph Bare(NodeType type) => new GraphBuilder()
        .Node("Users", NodeType.Client)
        .Node("Box", type)
        .Connect("Users", "Box")
        .Build();

    private static GraphBuilder Delegating(string configuration) => new GraphBuilder()
        .Node("Api", NodeType.Api, configuration)
        .Node("IdP", NodeType.IdentityProvider)
        .Node("Db", NodeType.Database)
        .Connect("Api", "IdP", timeoutSeconds: 1)
        .Connect("Api", "Db", timeoutSeconds: 5);

    private static ArchitectureGraph Bff(string configuration, int rps = 0) => new GraphBuilder()
        .Node("Web", NodeType.Client, rps == 0 ? "{}" : $$"""{"requestsPerSecond":{{rps}}}""")
        .Node("Bff", NodeType.Service, configuration)
        .Node("Db", NodeType.Database)
        .Connect("Web", "Bff")
        .Connect("Bff", "Db")
        .Build();

    private static ArchitectureGraph ApiAndDb(string configuration) => new GraphBuilder()
        .Node("Api", NodeType.Api, configuration)
        .Node("Db", NodeType.Database)
        .Connect("Api", "Db")
        .Build();

    private static ArchitectureGraph Gateway(string configuration) => new GraphBuilder()
        .Node("Gw", NodeType.Gateway, configuration)
        .Node("Db", NodeType.Database)
        .Connect("Gw", "Db")
        .Build();
}

public class NoObservabilityRuleTests
{
    [Fact]
    public void Every_component_nobody_watches_is_a_finding()
    {
        var findings = new NoObservabilityRule().Evaluate(Base().Build()).Findings;

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal("finding.NO_OBSERVABILITY.unobserved.message", f.Message.Key()));
        Assert.Equal(["Api", "Db"], findings.Select(f => f.Message.Arg("node")));

        Assert.All(findings, f => Assert.Equal("finding.NO_OBSERVABILITY.unobserved.suggestion.absent", f.Suggestion.Key()));
        Assert.All(findings, f => Assert.Null(f.Fix));
    }

    [Fact]
    public void Wiring_one_component_clears_that_component_and_no_other()
    {
        var graph = Base()
            .Node("Telemetry", NodeType.Observability)
            .Connect("Api", "Telemetry", synchronous: false)
            .Build();

        var finding = Assert.Single(new NoObservabilityRule().Evaluate(graph).Findings);

        Assert.Equal("Db", finding.Message.Arg("node"));
        Assert.Equal("finding.NO_OBSERVABILITY.unobserved.suggestion.present(node=Db, telemetry=Telemetry)", finding.Suggestion.Describe());

        Assert.NotNull(finding.Fix);
        Assert.Equal(FixKinds.ConnectTo, finding.Fix!.Kind);
        Assert.Equal(graph.NodeId("Db"), finding.Fix.Element.Id);
        Assert.Equal(graph.NodeId("Telemetry"), finding.Fix.TargetNodeId);
    }

    [Fact]
    public void Either_direction_counts_as_watching()
    {
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
        var bare = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Nodes(NodeType.Service, "A", "B")
            .Node("Db", NodeType.Database)
            .Node("Cola", NodeType.Queue)
            .Connect("Api", "A", timeoutSeconds: 3).Connect("Api", "B", timeoutSeconds: 3)
            .Connect("A", "Db", timeoutSeconds: 5)
            .Connect("Api", "Cola", synchronous: false);

        var wired = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Nodes(NodeType.Service, "A", "B")
            .Node("Db", NodeType.Database)
            .Node("Cola", NodeType.Queue)
            .Node("Telemetry", NodeType.Observability)
            .Connect("Api", "A", timeoutSeconds: 3).Connect("Api", "B", timeoutSeconds: 3)
            .Connect("A", "Db", timeoutSeconds: 5)
            .Connect("Api", "Cola", synchronous: false)
            .Connect("Api", "Telemetry", synchronous: false)
            .Connect("Telemetry", "A").Connect("Telemetry", "B")
            .Connect("Telemetry", "Db")
            .Connect("Cola", "Telemetry", synchronous: false);

        Assert.Empty(new HighCouplingRule().Evaluate(bare.Build()).Findings);
        Assert.Empty(new HighCouplingRule().Evaluate(wired.Build()).Findings);

        Assert.Empty(new MissingCacheRule().Evaluate(wired.Build()).Findings);

        Assert.Single(new QueueNoConsumerRule().Evaluate(bare.Build()).Findings);
        Assert.Single(new QueueNoConsumerRule().Evaluate(wired.Build()).Findings);
    }

    private static GraphBuilder Base() => new GraphBuilder()
        .Node("Api", NodeType.Api)
        .Node("Db", NodeType.Database)
        .Connect("Api", "Db");
}
