using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation;

/// <summary>
/// V4 DT.7 and plan técnico §5.4. The bound, and the properties that make it a bound rather than a
/// probability - which is the second correction of §2 and the one that kept a stochastic model from
/// sneaking into a simulator V3 declared deterministic.
/// </summary>
public class RequestYieldTests
{
    [Fact]
    public void Two_bottlenecks_in_series_give_the_worst_of_the_two_and_not_their_product()
    {
        // **The assertion that defines the metric.** Composing them as independent probabilities
        // would give 25 %; the bound gives 50 %, it can be redone in ten seconds, and it points at
        // something. It is optimistic on purpose and the panel says "como mucho".
        var graph = Loaded(600, builder => builder
            .Node("A", NodeType.Service)
            .Node("B", NodeType.Service)
            .Connect("Api", "A")
            .Connect("A", "B"));

        var (affected, bottleneck) = Impact(graph);

        Assert.Equal(0.5m, affected);
        Assert.Equal(FindingElement.Node(graph.NodeId("A")), bottleneck);
    }

    [Fact]
    public void A_timeout_and_a_fallback_contain_whatever_is_behind_them()
    {
        // Correction 3. Containment is read from the edge, not from phase A - a pure-load scenario
        // injects no fault, so phase A never runs, and `resilient-async` would have reported 100 %
        // affected with its database at 180 %.
        var graph = Loaded(300, builder => builder
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql));

        var (affected, bottleneck) = Impact(graph);

        Assert.Equal(0m, affected);
        Assert.Null(bottleneck);
    }

    [Fact]
    public void A_fallback_without_a_timeout_contains_nothing()
    {
        // V3 D4.2 kept intact: with no timeout the call never returns, so there is no error for the
        // fallback to catch. It is the guard that makes one checkbox the key to all the others.
        var graph = Loaded(300, builder => builder
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", fallback: true, protocol: ProtocolType.Sql));

        var (affected, _) = Impact(graph);

        Assert.Equal(0.3333m, affected);
    }

    [Fact]
    public void An_asynchronous_call_never_holds_a_request_back()
    {
        // V3 D4.7: nobody is waiting for an answer. What does not happen is the work afterwards,
        // and that travels as backlog, not as a lost request.
        var graph = Loaded(300, builder => builder
            .Node("Queue", NodeType.Queue)
            .Node("Worker", NodeType.Service)
            .Connect("Api", "Queue", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Queue", "Worker", synchronous: false, protocol: ProtocolType.Amqp));

        var (affected, _) = Impact(graph);

        Assert.Equal(0m, affected);
    }

    [Fact]
    public void A_dead_dependency_with_no_alternative_serves_nothing()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Build();

        var down = new HashSet<Guid> { graph.NodeId("Db") };
        var yield = RequestYield.Of(graph, LoadModel.Of(graph), new YieldInputs(down, new HashSet<Guid>()));

        var (affected, bottleneck) = yield.Impact();

        Assert.Equal(1m, affected);
        Assert.Equal(FindingElement.Node(graph.NodeId("Db")), bottleneck);
    }

    [Fact]
    public void A_call_that_expires_every_time_is_the_same_as_a_dead_target()
    {
        // V3 D4.1, and it is what composes the two axes in `worst-case`: nobody is killed, a
        // component is just slow enough that its caller's timeout always wins.
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Build();

        var expired = new HashSet<Guid> { graph.EdgeId("Api", "Db") };
        var yield = RequestYield.Of(graph, LoadModel.Of(graph), new YieldInputs(new HashSet<Guid>(), expired));

        Assert.Equal(1m, yield.Impact().Affected);
    }

    [Fact]
    public void A_cycle_does_not_hang_the_walk()
    {
        // Same cut as CallCost (V3 D5.4): the edge that closes the cycle yields nothing back.
        var graph = Loaded(100, builder => builder
            .Node("A", NodeType.Service)
            .Node("B", NodeType.Service)
            .Connect("Api", "A")
            .Connect("A", "B")
            .Connect("B", "A"));

        Assert.Equal(0m, Impact(graph).Affected);
    }

    [Fact]
    public void With_nothing_declared_and_nothing_broken_nothing_is_affected()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", protocol: ProtocolType.Sql)
            .Build();

        Assert.Equal(0m, Impact(graph).Affected);
    }

    private static (decimal Affected, FindingElement? Bottleneck) Impact(ArchitectureGraph graph) =>
        RequestYield.Of(graph, LoadModel.Of(graph), YieldInputs.CapacityOnly).Impact();

    private static ArchitectureGraph Loaded(int rps, Func<GraphBuilder, GraphBuilder> build) =>
        build(new GraphBuilder().Node("Api", NodeType.Api, $$"""{"requestsPerSecond":{{rps}}}""")).Build();
}
