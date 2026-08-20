using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation;

public class SimulationEngineTests
{
    private static SimulationResult Simulate(ArchitectureGraph graph, params Fault[] faults) =>
        new SimulationEngine().Run(graph, new SimulationScenario(faults));

    private static ElementState StateOf(SimulationResult result, GraphBuilder builder, string node) =>
        result.States.Single(s => s.Type == ElementType.Node && s.Id == builder.IdOf(node));

    // =========================================================================================
    // The four tests §11 says are worth writing even though they are not obvious.
    // =========================================================================================

    [Fact]
    public void ShufflingTheInputProducesAnIdenticalResult()
    {
        // This is what a shared link enseñando the same thing to someone else depends on (D1.2), and
        // it does not hold by itself: it is the double buffer, the id tie-breaks and the ordered
        // walks, each covering one place where a dictionary's iteration order could leak out.
        var (nodes, connections) = ReferenceArchitecture.Load("worst-case");
        var straight = new ArchitectureGraph(nodes, connections);
        var shuffled = new ArchitectureGraph(
            nodes.OrderByDescending(n => n.Name).ToList(),
            connections.OrderByDescending(c => c.Id).ToList());

        var a = Simulate(straight, new Fault(ElementType.Node, straight.NodeId("Gateway"), FaultKinds.Down));
        var b = Simulate(shuffled, new Fault(ElementType.Node, shuffled.NodeId("Gateway"), FaultKinds.Down));

        Assert.Equal(a.Metrics, b.Metrics);
        Assert.Equal(a.States, b.States);
    }

    [Fact]
    public void TheResultDoesNotDependOnWhichRulesAreRegistered()
    {
        // D5.2. Facts is the producer-to-consumer channel between rules; if the simulator read from
        // it, adding a rule in V4 would silently change what killing a node does. Without this test
        // Facts ends up creeping in - it is right there on the graph the engine already holds.
        var clean = ReferenceArchitecture.Graph("worst-case");
        var analyzed = ReferenceArchitecture.Graph("worst-case");

        new ArchitectureAnalyzer([
            new SpofRule(), new CycleRule(), new RetryAmplificationRule(), new SyncDependencyChainRule(),
            new MissingResilienceRule(), new HighCouplingRule(), new MissingCacheRule()
        ]).Analyze(analyzed);

        Assert.NotEmpty(analyzed.Facts);

        var withoutRules = Simulate(clean, new Fault(ElementType.Node, clean.NodeId("Db"), FaultKinds.Down));
        var withRules = Simulate(analyzed, new Fault(ElementType.Node, analyzed.NodeId("Db"), FaultKinds.Down));

        Assert.Equal(withoutRules.Metrics, withRules.Metrics);
        Assert.Equal(withoutRules.States, withRules.States);
    }

    [Fact]
    public void EveryCausedByChainEndsAtAnInjectedFault()
    {
        // D2.2. It holds because an edge's state in round n comes from node states of round n-1, so
        // the step drops strictly every two hops - and it holds over worst-case, which has a
        // synchronous cycle. Exactly the kind of invariant that breaks in silence.
        var graph = ReferenceArchitecture.Graph("worst-case");
        var injected = graph.EdgeId("Orders", "Shipping");

        var result = Simulate(graph, new Fault(ElementType.Connection, injected, FaultKinds.Down));
        var byElement = result.States.ToDictionary(s => new FindingElement(s.Type, s.Id));

        foreach (var state in result.States.Where(s => s.State != HealthState.Healthy))
        {
            var current = state;

            for (var hop = 0; current.CausedBy is not null; hop++)
            {
                Assert.True(hop <= result.States.Count, "The CausedBy chain does not terminate.");
                current = byElement[current.CausedBy];
            }

            // Whatever it was, following the causes backwards lands on the element the user pointed
            // at - which is what lets the panel group by root cause instead of listing N identical
            // lines (D4.9).
            Assert.Equal(injected, current.Id);
        }
    }

    [Fact]
    public void NoUnreachableComponentHasADependentStillStanding()
    {
        // The claim that lets two phases be enough (§6.2): if a dependent of X is reachable then so
        // is X through it, so phase B can only mark nodes whose dependents it already marked, and a
        // second pass would change nothing. If this ever fails, phase B needs to iterate.
        var graph = ReferenceArchitecture.Graph("worst-case");
        var result = Simulate(graph, new Fault(ElementType.Node, graph.NodeId("Gateway"), FaultKinds.Down));

        var unreachable = result.States
            .Where(s => s.Type == ElementType.Node && s.Reason.Es().StartsWith("No le llega tráfico"))
            .Select(s => s.Id)
            .ToHashSet();

        Assert.NotEmpty(unreachable);

        var byNode = result.States.Where(s => s.Type == ElementType.Node).ToDictionary(s => s.Id);

        foreach (var nodeId in unreachable)
        {
            foreach (var dependent in graph.IncomingByTarget[nodeId])
                Assert.NotEqual(HealthState.Healthy, byNode[dependent.SourceNodeId].State);
        }
    }

    // =========================================================================================
    // The empty scenario, and the faults that no longer point anywhere.
    // =========================================================================================

    [Fact]
    public void TheEmptyScenarioIsValidAndAnswersInGreen()
    {
        // D2.1: entering Simulate mode before choosing anything has to show the architecture in
        // green with the metrics at rest, not an error and not an empty panel.
        var graph = ReferenceArchitecture.Graph("gateway-microservices");

        var result = Simulate(graph);

        Assert.All(result.States, state => Assert.Equal(HealthState.Healthy, state.State));
        Assert.Equal(0, result.Metrics.ComponentsDown);
        Assert.Equal(0m, result.Metrics.BlastRadius);
        Assert.Equal(0, result.Metrics.EntryPointsAffected);
        Assert.Equal(1m, result.Metrics.RequestAmplification);

        // Under no fault the two latencies are the same figure, which is what makes the panel's
        // comparison legible the moment the first fault lands.
        Assert.Equal(result.Metrics.NominalLatencyMs, result.Metrics.FaultedLatencyMs);
    }

    [Fact]
    public void AFaultOverAnElementThatNoLongerExistsIsDroppedAndAnnounced()
    {
        // D3.6: an old link over an architecture that has been edited since must still open. Never
        // an error - but never in silence either.
        var graph = ReferenceArchitecture.Graph("minimal-api-db");
        var ghost = new Fault(ElementType.Node, Guid.NewGuid(), FaultKinds.Down);

        var result = Simulate(graph, ghost, new Fault(ElementType.Node, graph.NodeId("Db"), FaultKinds.Down));

        Assert.Equal([ghost], result.DiscardedFaults);
        Assert.Equal(2, result.Metrics.ComponentsDown);
    }

    // =========================================================================================
    // Mechanisms that only show up away from the four fixtures.
    // =========================================================================================

    [Fact]
    public void ABalancerWithAHealthCheckStopsTheFaultAtItself()
    {
        // D4.8. The visual argument for why health checks exist: ticking the box changes the colour
        // of everything above the balancer.
        var builder = new GraphBuilder()
            .Node("Edge", NodeType.Gateway)
            .Node("LB", NodeType.LoadBalancer, """{"healthCheck":true}""")
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Connect("Edge", "LB", timeoutSeconds: 5)
            .Connect("LB", "Api1", timeoutSeconds: 5)
            .Connect("LB", "Api2", timeoutSeconds: 5);

        var graph = builder.Build();
        var result = Simulate(graph, new Fault(ElementType.Node, builder.IdOf("Api1"), FaultKinds.Down));

        Assert.Equal(HealthState.Degraded, StateOf(result, builder, "LB").State);
        Assert.Equal("healthCheck", StateOf(result, builder, "LB").Mechanism);
        Assert.Equal(HealthState.Healthy, StateOf(result, builder, "Edge").State);
    }

    [Fact]
    public void WithoutAHealthCheckTheDegradationClimbsToTheRoot()
    {
        var builder = new GraphBuilder()
            .Node("Edge", NodeType.Gateway)
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Connect("Edge", "LB", timeoutSeconds: 5)
            .Connect("LB", "Api1", timeoutSeconds: 5)
            .Connect("LB", "Api2", timeoutSeconds: 5);

        var graph = builder.Build();
        var result = Simulate(graph, new Fault(ElementType.Node, builder.IdOf("Api1"), FaultKinds.Down));

        Assert.Equal(HealthState.Degraded, StateOf(result, builder, "LB").State);
        Assert.Null(StateOf(result, builder, "LB").Mechanism);
        Assert.Equal(HealthState.Degraded, StateOf(result, builder, "Edge").State);
    }

    [Fact]
    public void AConsumerOfAFallenQueueIsIdleRatherThanDead()
    {
        // D4.7, and the distinction is part of what the simulator teaches: the producer keeps
        // serving and contains, and the consumer is alive with nothing to do.
        var builder = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Bus", NodeType.Queue)
            .Node("Worker", NodeType.Service)
            .Connect("Api", "Bus", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Bus", "Worker", synchronous: false, protocol: ProtocolType.Amqp);

        var graph = builder.Build();
        var result = Simulate(graph, new Fault(ElementType.Node, builder.IdOf("Bus"), FaultKinds.Down));

        Assert.Equal(HealthState.Degraded, StateOf(result, builder, "Api").State);
        Assert.Equal("queue", StateOf(result, builder, "Api").Mechanism);
        Assert.Contains("sigue atendiendo peticiones", StateOf(result, builder, "Api").Reason.Es());

        Assert.Equal(HealthState.Degraded, StateOf(result, builder, "Worker").State);
        Assert.Contains("No le llega trabajo", StateOf(result, builder, "Worker").Reason.Es());
    }

    [Fact]
    public void AnInjectedDegradedFaultDoesNotContain()
    {
        // D4.1: it answers worse and drags the degradation upwards, which is what makes it a fault
        // and not a mechanism. Otherwise it would be indistinguishable from a fallback.
        var builder = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Orders", NodeType.Service)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Orders", timeoutSeconds: 5)
            .Connect("Orders", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var graph = builder.Build();
        var result = Simulate(graph, new Fault(ElementType.Node, builder.IdOf("Db"), FaultKinds.Degraded));

        Assert.Equal(HealthState.Degraded, StateOf(result, builder, "Db").State);
        Assert.Equal(HealthState.Degraded, StateOf(result, builder, "Orders").State);
        Assert.Equal(HealthState.Degraded, StateOf(result, builder, "Api").State);
        Assert.All(result.States, state => Assert.Null(state.Mechanism));
    }

    [Fact]
    public void AFallbackStopsTheFaultAndWhatIsAboveStaysGreen()
    {
        // The picture of §10.2 turned into an assertion: red at the core, amber at the border,
        // green beyond. This is the rule the whole product rests on (D4.5).
        var builder = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Orders", NodeType.Service)
            .Node("Payments", NodeType.Service)
            .Connect("Api", "Orders", timeoutSeconds: 5)
            .Connect("Orders", "Payments", timeoutSeconds: 5, fallback: true);

        var graph = builder.Build();
        var result = Simulate(graph, new Fault(ElementType.Node, builder.IdOf("Payments"), FaultKinds.Down));

        Assert.Equal(HealthState.Down, StateOf(result, builder, "Payments").State);
        Assert.Equal(HealthState.Degraded, StateOf(result, builder, "Orders").State);
        Assert.Equal("fallback", StateOf(result, builder, "Orders").Mechanism);
        Assert.Equal(HealthState.Healthy, StateOf(result, builder, "Api").State);
    }

    [Fact]
    public void ComposingFaultsDoesNotDependOnTheOrderTheyAreWrittenIn()
    {
        // D2.1: the lattice is monotone, so the resulting state is the maximum. Two links with the
        // faults written in a different order are the same experiment.
        var graph = ReferenceArchitecture.Graph("worst-case");
        var db = new Fault(ElementType.Node, graph.NodeId("Db"), FaultKinds.Down);
        var latency = new Fault(ElementType.Connection, graph.EdgeId("Orders", "Shipping"), FaultKinds.Latency, 8_000);

        var a = Simulate(graph, db, latency);
        var b = Simulate(ReferenceArchitecture.Graph("worst-case"), latency, db);

        Assert.Equal(a.States, b.States);
        Assert.Equal(a.Metrics, b.Metrics);
    }

    [Fact]
    public void SeveringAnEdgeIsAPartition_TheTargetKeepsServingEverybodyElse()
    {
        // D4.1: `down` over an edge models something killing a node does not - a dependency that is
        // unreachable from one caller but alive and serving the rest. Killing Db would take both
        // APIs with it; partitioning one call takes only that caller.
        var builder = new GraphBuilder()
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Node("Db", NodeType.Database)
            .Connect("Api1", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Api2", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var graph = builder.Build();
        var result = Simulate(graph, new Fault(ElementType.Connection, builder.EdgeId("Api1", "Db"), FaultKinds.Down));

        Assert.Equal(HealthState.Down, StateOf(result, builder, "Api1").State);
        Assert.Equal(HealthState.Healthy, StateOf(result, builder, "Db").State);
        Assert.Equal(HealthState.Healthy, StateOf(result, builder, "Api2").State);
    }

    [Fact]
    public void SeveringTheOnlyWayInLeavesTheTargetWithoutTraffic()
    {
        // The other half of the same rule, worth pinning because it is where "alive" and "serving"
        // come apart. The node was not killed - nothing was injected into it - but once its only
        // way in is cut, no traffic reaches it, and phase B says so with those words rather than
        // pretending it is still doing its job.
        var builder = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var graph = builder.Build();
        var result = Simulate(graph, new Fault(ElementType.Connection, builder.EdgeId("Api", "Db"), FaultKinds.Down));

        Assert.Equal(HealthState.Down, StateOf(result, builder, "Api").State);
        Assert.Equal(HealthState.Down, StateOf(result, builder, "Db").State);
        Assert.Contains("No le llega tráfico", StateOf(result, builder, "Db").Reason.Es());
    }

    [Fact]
    public void StatesArriveOrderedByPropagationStep()
    {
        // D5.6: the list already reads as the order of propagation, so the animation of D6.3 only
        // has to replay it.
        var graph = ReferenceArchitecture.Graph("worst-case");
        var result = Simulate(graph, new Fault(ElementType.Node, graph.NodeId("Gateway"), FaultKinds.Down));

        Assert.Equal(
            result.States.Select(s => s.PropagationStep).OrderBy(step => step),
            result.States.Select(s => s.PropagationStep));
    }
}
