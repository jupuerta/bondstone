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


    [Fact]
    public void ShufflingTheInputProducesAnIdenticalResult()
    {
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

            Assert.Equal(injected, current.Id);
        }
    }

    [Fact]
    public void NoUnreachableComponentHasADependentStillStanding()
    {
        var graph = ReferenceArchitecture.Graph("worst-case");
        var result = Simulate(graph, new Fault(ElementType.Node, graph.NodeId("Gateway"), FaultKinds.Down));

        var unreachable = result.States
            .Where(s => s.Type == ElementType.Node && s.Reason.Key() == "sim.node.unreachable")
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


    [Fact]
    public void TheEmptyScenarioIsValidAndAnswersInGreen()
    {
        var graph = ReferenceArchitecture.Graph("gateway-microservices");

        var result = Simulate(graph);

        Assert.All(result.States, state => Assert.Equal(HealthState.Healthy, state.State));
        Assert.Equal(0, result.Metrics.ComponentsDown);
        Assert.Equal(0m, result.Metrics.BlastRadius);
        Assert.Equal(0, result.Metrics.EntryPointsAffected);
        Assert.Equal(1m, result.Metrics.RequestAmplification);

        Assert.Equal(result.Metrics.NominalLatencyMs, result.Metrics.FaultedLatencyMs);
    }

    [Fact]
    public void AFaultOverAnElementThatNoLongerExistsIsDroppedAndAnnounced()
    {
        var graph = ReferenceArchitecture.Graph("minimal-api-db");
        var ghost = new Fault(ElementType.Node, Guid.NewGuid(), FaultKinds.Down);

        var result = Simulate(graph, ghost, new Fault(ElementType.Node, graph.NodeId("Db"), FaultKinds.Down));

        Assert.Equal([ghost], result.DiscardedFaults);
        Assert.Equal(2, result.Metrics.ComponentsDown);
    }


    [Fact]
    public void ABalancerWithAHealthCheckStopsTheFaultAtItself()
    {
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
        var builder = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Bus", NodeType.Queue)
            .Node("Worker", NodeType.Service)
            .Connect("Api", "Bus", synchronous: false)
            .Connect("Bus", "Worker", synchronous: false);

        var graph = builder.Build();
        var result = Simulate(graph, new Fault(ElementType.Node, builder.IdOf("Bus"), FaultKinds.Down));

        Assert.Equal(HealthState.Degraded, StateOf(result, builder, "Api").State);
        Assert.Equal("queue", StateOf(result, builder, "Api").Mechanism);
        Assert.Equal("sim.node.queueDown", StateOf(result, builder, "Api").Reason.Key());

        Assert.Equal(HealthState.Degraded, StateOf(result, builder, "Worker").State);
        Assert.Equal("sim.node.noWork", StateOf(result, builder, "Worker").Reason.Key());
    }

    [Fact]
    public void AnInjectedDegradedFaultDoesNotContain()
    {
        var builder = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Orders", NodeType.Service)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Orders", timeoutSeconds: 5)
            .Connect("Orders", "Db", timeoutSeconds: 5);

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
        var builder = new GraphBuilder()
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Node("Db", NodeType.Database)
            .Connect("Api1", "Db", timeoutSeconds: 5)
            .Connect("Api2", "Db", timeoutSeconds: 5);

        var graph = builder.Build();
        var result = Simulate(graph, new Fault(ElementType.Connection, builder.EdgeId("Api1", "Db"), FaultKinds.Down));

        Assert.Equal(HealthState.Down, StateOf(result, builder, "Api1").State);
        Assert.Equal(HealthState.Healthy, StateOf(result, builder, "Db").State);
        Assert.Equal(HealthState.Healthy, StateOf(result, builder, "Api2").State);
    }

    [Fact]
    public void SeveringTheOnlyWayInLeavesTheTargetWithoutTraffic()
    {
        var builder = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 5);

        var graph = builder.Build();
        var result = Simulate(graph, new Fault(ElementType.Connection, builder.EdgeId("Api", "Db"), FaultKinds.Down));

        Assert.Equal(HealthState.Down, StateOf(result, builder, "Api").State);
        Assert.Equal(HealthState.Down, StateOf(result, builder, "Db").State);
        Assert.Equal("sim.node.unreachable", StateOf(result, builder, "Db").Reason.Key());
    }

    [Fact]
    public void StatesArriveOrderedByPropagationStep()
    {
        var graph = ReferenceArchitecture.Graph("worst-case");
        var result = Simulate(graph, new Fault(ElementType.Node, graph.NodeId("Gateway"), FaultKinds.Down));

        Assert.Equal(
            result.States.Select(s => s.PropagationStep).OrderBy(step => step),
            result.States.Select(s => s.PropagationStep));
    }
    // QA 30-08 §10.3, point 13, decided that day: «degrade it» used to walk straight up through a
    // call carrying timeout, breaker and fallback, with mechanism «none» — the workshop card promised
    // the fallback «cuts the failure backwards» and nothing did. A slow dependency is the failure a
    // fallback does not stop: you keep calling, keep waiting and answer with whatever arrives. The
    // breaker does stop it, because it trips and stops calling.
    [Theory]
    [InlineData(true, "circuitBreaker")]
    [InlineData(false, null)]
    public void A_breaker_is_what_contains_a_degraded_dependency(bool breaker, string? mechanism)
    {
        var graph = new GraphBuilder()
            .Node("Storefront", NodeType.Api, """{"requestsPerSecond":100,"authentication":true}""")
            .Node("Orders", NodeType.Service)
            .Connect("Storefront", "Orders", timeoutSeconds: 3, circuitBreaker: breaker, fallback: true)
            .Build();

        var result = new SimulationEngine().Run(graph, new SimulationScenario(
            [new Fault(ElementType.Node, graph.NodeId("Orders"), FaultKinds.Degraded)]));

        var storefront = result.States.Single(e => e.Id == graph.NodeId("Storefront"));

        Assert.Equal(HealthState.Degraded, storefront.State);
        Assert.Equal(mechanism, storefront.Mechanism);
    }

}
