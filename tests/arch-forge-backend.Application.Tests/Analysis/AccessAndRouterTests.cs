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
