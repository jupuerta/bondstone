using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;

namespace arch_forge_backend.Application.Tests.Simulation.Scenarios;

public sealed class ReferenceScenario
{
    private ReferenceScenario(ArchitectureGraph graph, SimulationResult result)
    {
        Graph = graph;
        Result = result;
    }

    public ArchitectureGraph Graph { get; }
    public SimulationResult Result { get; }
    public SimulationMetrics Metrics => Result.Metrics;

    public static ReferenceScenario Run(string fixtureName, params Fault[] faults)
    {
        var graph = ReferenceArchitecture.Graph(fixtureName);
        return new ReferenceScenario(graph, new SimulationEngine().Run(graph, new SimulationScenario(faults)));
    }

    public static Fault NodeDown(ArchitectureGraph graph, string name) =>
        new(ElementType.Node, graph.NodeId(name), FaultKinds.Down);

    public static Fault EdgeLatency(ArchitectureGraph graph, string source, string target, int ms) =>
        new(ElementType.Connection, graph.EdgeId(source, target), FaultKinds.Latency, ms);

    public ElementState Node(string name) =>
        Result.States.Single(s => s.Type == ElementType.Node && s.Id == Graph.NodeId(name));

    public ElementState Edge(string source, string target) =>
        Result.States.Single(s => s.Type == ElementType.Connection && s.Id == Graph.EdgeId(source, target));

    public IEnumerable<string> NodesIn(HealthState state) => Result.States
        .Where(s => s.Type == ElementType.Node && s.State == state)
        .Select(s => Graph.NodesById[s.Id].Name)
        .OrderBy(name => name, StringComparer.Ordinal);

    public IEnumerable<string> Contained() => Result.States
        .Where(s => s.Type == ElementType.Node && s.Mechanism is not null)
        .Select(s => $"{Graph.NodesById[s.Id].Name}:{s.Mechanism}")
        .OrderBy(entry => entry, StringComparer.Ordinal);

    public string NameOf(FindingElement element) => element.Type == ElementType.Node
        ? Graph.NodesById[element.Id].Name
        : Graph.Connections.Where(c => c.Id == element.Id)
            .Select(c => $"{Graph.NodesById[c.SourceNodeId].Name}->{Graph.NodesById[c.TargetNodeId].Name}")
            .Single();
}
