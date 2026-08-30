using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis;

// QA 30-08 §7.4: the model reads `access` where nobody read it for this, and a router has nothing
// to answer with. Every case here was a mechanism the model believed unconditionally (§3.5, §1.3).
public class AccessAndRouterTests
{
    private static ArchitectureGraph Graph(GraphBuilder builder) => builder.Build();

    private static SimulationMetrics Losing(ArchitectureGraph graph, string name) =>
        new SimulationEngine()
            .Run(graph, new SimulationScenario([new Fault(ElementType.Node, graph.Nodes.Single(n => n.Name == name).Id, FaultKinds.Down)]))
            .Metrics;

    [Fact]
    public void A_fallback_on_a_declared_write_serves_nothing()
    {
        static GraphBuilder Shop(string? access) => new GraphBuilder()
            .Node("Shop", NodeType.Api, """{"requestsPerSecond":100}""").Node("Pay", NodeType.Service)
            .Connect("Shop", "Pay", timeoutSeconds: 2, fallback: true, access: access);

        Assert.Equal(0m, Losing(Graph(Shop(null)), "Pay").RequestsAffected);
        Assert.Equal(0m, Losing(Graph(Shop("read")), "Pay").RequestsAffected);
        Assert.Equal(1m, Losing(Graph(Shop("write")), "Pay").RequestsAffected);
    }

    // Two shops share the payment service: with the fallback believed, nobody depends on it hard
    // and it is nobody's single point of failure; declared as a write, both do.
    [Fact]
    public void A_fallback_on_a_declared_write_is_still_a_hard_dependency()
    {
        static GraphBuilder Shops(string? access) => new GraphBuilder()
            .Node("Shop", NodeType.Api, """{"requestsPerSecond":100,"authentication":true}""")
            .Node("Shop 2", NodeType.Api, """{"requestsPerSecond":100,"authentication":true}""")
            .Node("Pay", NodeType.Service).Node("Db", NodeType.Database, """{"replicas":1}""")
            .Connect("Shop", "Pay", timeoutSeconds: 2, fallback: true, access: access)
            .Connect("Shop 2", "Pay", timeoutSeconds: 2, fallback: true, access: access)
            .Connect("Pay", "Db", timeoutSeconds: 2);

        Assert.DoesNotContain(new SpofRule().Evaluate(Graph(Shops(null))).Findings, f => f.Message.Arg("node") == "Pay");
        Assert.Contains(new SpofRule().Evaluate(Graph(Shops("write"))).Findings, f => f.Message.Arg("node") == "Pay");
    }

    [Fact]
    public void Read_replicas_do_not_absorb_declared_writes()
    {
        static ArchitectureGraph Writers(string? access) => Graph(new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":500}""").Node("Db", NodeType.Database, """{"readReplicas":3}""")
            .Connect("Api", "Db", timeoutSeconds: 3, access: access));

        var reads = LoadModel.Of(Writers(null));
        var writes = LoadModel.Of(Writers("write"));
        var db = writes.CapacityOf(Writers("write").Nodes.Single(n => n.Type == NodeType.Database).Id);

        Assert.Equal(800m, db);
        Assert.Equal(0.625m, reads.UtilizationOf(Writers(null).Nodes.Single(n => n.Type == NodeType.Database).Id));

        var target = Writers("write").Nodes.Single(n => n.Type == NodeType.Database).Id;
        Assert.Equal(2.5m, writes.UtilizationOf(target));
        Assert.True(writes.WritesBind(target));
        Assert.Equal((500m, 200m), writes.BindingOf(target));
    }

    [Fact]
    public void The_overloaded_store_says_it_is_the_writes_and_not_the_load()
    {
        var graph = Graph(new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":500}""").Node("Db", NodeType.Database, """{"readReplicas":3}""")
            .Connect("Api", "Db", timeoutSeconds: 3, access: "write"));

        var finding = Assert.Single(new UndersizedComponentRule().Evaluate(graph).Findings, f => f.Message.Arg("node") == "Db");

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.UNDERSIZED_COMPONENT.overloaded.explanation.writes", finding.Explanation.Key());
        Assert.Equal("500", finding.Explanation.Arg("loadRps"));
        Assert.Equal("200", finding.Explanation.Arg("capacityRps"));
        Assert.Equal("finding.UNDERSIZED_COMPONENT.suggestion.databaseWrites", finding.Suggestion.Key());
    }

    [Fact]
    public void A_full_replica_takes_the_writes_a_read_replica_does_not()
    {
        var graph = Graph(new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":500}""").Node("Db", NodeType.Database, """{"replicas":3}""")
            .Connect("Api", "Db", timeoutSeconds: 3, access: "write"));

        Assert.Equal(0.625m, LoadModel.Of(graph).UtilizationOf(graph.Nodes.Single(n => n.Type == NodeType.Database).Id));
    }

    [Fact]
    public void A_fallback_on_a_router_does_not_exist_for_the_model()
    {
        var graph = Graph(new GraphBuilder()
            .Node("Edge", NodeType.Gateway, """{"requestsPerSecond":100,"authentication":true}""").Node("Front", NodeType.LoadBalancer)
            .Node("Api", NodeType.Api).Node("Db", NodeType.Database).Node("Cache", NodeType.Cache)
            .Connect("Edge", "Front", timeoutSeconds: 5)
            .Connect("Front", "Api", timeoutSeconds: 5, fallback: true)
            .Connect("Api", "Cache", timeoutSeconds: 2)
            .Connect("Api", "Db", timeoutSeconds: 5));

        var call = graph.Connections.Single(c => graph.NodesById[c.SourceNodeId].Name == "Front");

        Assert.True(graph.ResilienceOf(call).Fallback);
        Assert.False(graph.EffectiveResilienceOf(call).Fallback);

        var metrics = Losing(graph, "Api");

        Assert.Equal(1, metrics.EntryPointsAffected);
        Assert.Equal(1m, metrics.RequestsAffected);

        Assert.Contains(new SpofRule().Evaluate(graph).Findings, f => f.Message.Arg("node") == "Api");
    }

    // QA 30-08 §10.3, point 4: on a declared write the button offered a fallback, MISSING_RESILIENCE
    // called the call protected, and the simulator lost every request anyway. Now the button offers
    // what the model believes — timeout and breaker — and the sentence says queue.
    [Fact]
    public void The_button_on_a_declared_write_offers_a_breaker_and_the_sentence_says_queue()
    {
        // Three writers: a shared dependency is critical from three dependents (SpofRule).
        static ArchitectureGraph Writers(int? timeout) => Graph(new GraphBuilder()
            .Node("Gw", NodeType.Gateway, """{"requestsPerSecond":100,"authentication":true}""")
            .Nodes(NodeType.Service, "Orders", "Invoicing", "Reports").Node("Db", NodeType.Database)
            .Connect("Gw", "Orders", timeoutSeconds: 3).Connect("Gw", "Invoicing", timeoutSeconds: 3).Connect("Gw", "Reports", timeoutSeconds: 3)
            .Connect("Orders", "Db", timeoutSeconds: timeout, access: "write")
            .Connect("Invoicing", "Db", timeoutSeconds: timeout, access: "write")
            .Connect("Reports", "Db", timeoutSeconds: timeout, access: "write"));

        foreach (var (timeout, suggestion) in new[] { ((int?)null, "noTimeoutAndSpof"), (3, "exposedToSpof") })
        {
            var graph = Writers(timeout);
            var writes = ReferenceArchitecture.Analyze(graph).Findings
                .Where(f => f.Code == RuleCodes.MissingResilience && f.Severity == Severity.Critical)
                .ToList();

            Assert.Equal(3, writes.Count);
            Assert.All(writes, f =>
            {
                Assert.Equal($"finding.MISSING_RESILIENCE.{suggestion}.suggestion.write", f.Suggestion!.Key());
                Assert.True(f.Fix!.CircuitBreaker);
                Assert.Null(f.Fix.Fallback);
                Assert.Equal(timeout is null ? 5 : null, f.Fix.TimeoutSeconds);
            });
        }
    }

    [Fact]
    public void A_fallback_on_a_declared_write_is_the_drawing_talking()
    {
        static ArchitectureGraph Shop(string? access) => Graph(new GraphBuilder()
            .Node("Shop", NodeType.Api, """{"requestsPerSecond":100,"authentication":true}""").Node("Db", NodeType.Database)
            .Connect("Shop", "Db", timeoutSeconds: 2, fallback: true, access: access));

        Assert.Empty(new ImplausibleDependencyRule().Evaluate(Shop(null)).Findings);
        Assert.Empty(new ImplausibleDependencyRule().Evaluate(Shop("read")).Findings);

        var finding = Assert.Single(new ImplausibleDependencyRule().Evaluate(Shop("write")).Findings);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.IMPLAUSIBLE_DEPENDENCY.writeFallback.message", finding.Message.Key());
        Assert.Equal("Shop", finding.Message.Arg("source"));
        Assert.Equal("Db", finding.Suggestion!.Arg("target"));
    }

    // QA 30-08 §10.3, point 5: the automatic cache rerouted every call, declared writes included, and
    // SHARED_DATABASE went quiet because the schema was now «behind a box». The button reroutes only
    // the reads, and the rule reads through the cache the way it reads through a balancer.
    [Fact]
    public void Services_sharing_a_schema_through_a_cache_still_share_it()
    {
        static GraphBuilder Readers(string? access) => new GraphBuilder()
            .Node("Gw", NodeType.Gateway, """{"requestsPerSecond":100,"authentication":true}""")
            .Nodes(NodeType.Service, "Orders", "Invoicing").Node("Db", NodeType.Database)
            .Connect("Gw", "Orders", timeoutSeconds: 3).Connect("Gw", "Invoicing", timeoutSeconds: 3)
            .Connect("Orders", "Db", timeoutSeconds: 3, access: access)
            .Connect("Invoicing", "Db", timeoutSeconds: 3, access: access);

        static ArchitectureGraph Cached(string? access) => Graph(new GraphBuilder()
            .Node("Gw", NodeType.Gateway, """{"requestsPerSecond":100,"authentication":true}""")
            .Nodes(NodeType.Service, "Orders", "Invoicing").Node("Cache", NodeType.Cache).Node("Db", NodeType.Database)
            .Connect("Gw", "Orders", timeoutSeconds: 3).Connect("Gw", "Invoicing", timeoutSeconds: 3)
            .Connect("Orders", "Cache", timeoutSeconds: 3, fallback: true, access: access)
            .Connect("Invoicing", "Cache", timeoutSeconds: 3, fallback: true, access: access)
            .Connect("Cache", "Db", timeoutSeconds: 5, access: "read"));

        foreach (var access in new[] { null, "read" })
        {
            var before = Assert.Single(new SharedDatabaseRule().Evaluate(Graph(Readers(access))).Findings);
            var after = Assert.Single(new SharedDatabaseRule().Evaluate(Cached(access)).Findings);

            Assert.Equal(before.Message.Key(), after.Message.Key());
            Assert.Equal(before.Message.Arg("names"), after.Message.Arg("names"));
        }

        Assert.Single(new MissingCacheRule().Evaluate(Graph(Readers(null))).Findings);
        Assert.Empty(new MissingCacheRule().Evaluate(Cached(null)).Findings);
    }

    // The counterpart of «reroute only the reads»: a call that also writes is one arrow, and the
    // button cannot send half of it through a cache, so the rule does not ask for one.
    [Fact]
    public void A_call_that_also_writes_cannot_go_behind_a_cache_so_it_is_not_counted()
    {
        var both = Graph(new GraphBuilder()
            .Nodes(NodeType.Service, "Orders", "Invoicing").Node("Db", NodeType.Database)
            .Connect("Orders", "Db", timeoutSeconds: 3, access: "both")
            .Connect("Invoicing", "Db", timeoutSeconds: 3, access: "both"));

        Assert.Empty(new MissingCacheRule().Evaluate(both).Findings);
        Assert.Equal("finding.SHARED_DATABASE.writers.message", Assert.Single(new SharedDatabaseRule().Evaluate(both).Findings).Message.Key());
    }

    // QA 30-08 §10.2, point 2: with the Api a critical SPOF, ticking a fallback on the balancer's
    // call turned a critical MISSING_RESILIENCE (−15) into an IMPLAUSIBLE warning (+5) and the score
    // went up, by a box the model says is not there. The two rules read the same fallback the
    // simulator reads now, so the critical stays and the warning is added on top.
    [Fact]
    public void A_fallback_a_router_cannot_honour_does_not_buy_the_call_out_of_its_finding()
    {
        static ArchitectureGraph Shop(bool fallback) => Graph(new GraphBuilder()
            .Node("Edge", NodeType.Gateway, """{"requestsPerSecond":100,"authentication":true}""")
            .Node("Front", NodeType.LoadBalancer).Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database, """{"replicas":2}""").Node("Cache", NodeType.Cache)
            .Connect("Edge", "Front", timeoutSeconds: 5)
            .Connect("Front", "Api", timeoutSeconds: 5, fallback: fallback)
            .Connect("Api", "Cache", timeoutSeconds: 2, fallback: true)
            .Connect("Api", "Db", timeoutSeconds: 5, fallback: true));

        var bare = ReferenceArchitecture.Analyze(Shop(false));
        var ticked = ReferenceArchitecture.Analyze(Shop(true));

        Assert.Contains(bare.Findings, f => f.Code == RuleCodes.MissingResilience && f.Severity == Severity.Critical);
        Assert.Contains(ticked.Findings, f => f.Code == RuleCodes.MissingResilience && f.Severity == Severity.Critical);
        Assert.Contains(ticked.Findings, f => f.Message.Key() == "finding.IMPLAUSIBLE_DEPENDENCY.routerFallback.message");

        Assert.True(ticked.Score <= bare.Score, $"ticking a box the model does not believe raised the score: {bare.Score} → {ticked.Score}.");
    }

    [Fact]
    public void A_long_timeout_towards_a_critical_spof_is_not_covered_by_a_write_fallback()
    {
        static ArchitectureGraph Shop(string? access) => Graph(new GraphBuilder()
            .Node("Gw", NodeType.Gateway, """{"requestsPerSecond":100,"authentication":true}""")
            .Nodes(NodeType.Service, "Orders", "Invoicing", "Reports").Node("Db", NodeType.Database)
            .Connect("Gw", "Orders", timeoutSeconds: 3).Connect("Gw", "Invoicing", timeoutSeconds: 3).Connect("Gw", "Reports", timeoutSeconds: 3)
            .Connect("Orders", "Db", timeoutSeconds: 600, fallback: true, access: access)
            .Connect("Invoicing", "Db", timeoutSeconds: 3, access: access)
            .Connect("Reports", "Db", timeoutSeconds: 3, access: access));

        Severity LongCall(string? access) => Assert.Single(
            ReferenceArchitecture.Analyze(Shop(access)).Findings,
            f => f.Code == RuleCodes.TimeoutTooLong).Severity;

        Assert.Equal(Severity.Warning, LongCall(null));
        Assert.Equal(Severity.Critical, LongCall("write"));
    }

    [Fact]
    public void The_button_on_a_routers_call_offers_a_breaker_and_never_a_fallback()
    {
        var graph = Graph(new GraphBuilder()
            .Node("Edge", NodeType.Gateway, """{"requestsPerSecond":100,"authentication":true}""")
            .Node("Api", NodeType.Api).Node("Db", NodeType.Database)
            .Connect("Edge", "Api")
            .Connect("Api", "Db"));

        var findings = ReferenceArchitecture.Analyze(graph).Findings;

        var routers = findings.Where(f => f.Code == RuleCodes.MissingResilience && f.Fix is not null)
            .Select(f => f.Fix!)
            .Where(fix => fix.Element.Id == graph.Connections.Single(c => graph.NodesById[c.SourceNodeId].Name == "Edge").Id)
            .ToList();

        Assert.NotEmpty(routers);
        Assert.All(routers, fix =>
        {
            Assert.True(fix.CircuitBreaker);
            Assert.Null(fix.Fallback);
            Assert.Contains("CircuitBreaker", fix.Label.Key());
        });
    }
}
