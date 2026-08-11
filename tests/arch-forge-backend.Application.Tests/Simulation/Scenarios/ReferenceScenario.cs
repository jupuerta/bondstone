using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;

namespace arch_forge_backend.Application.Tests.Simulation.Scenarios;

/// <summary>
/// Runs the real engine over one of V2's four reference architectures and lets the assertions read
/// by component name. The expected numbers were worked out by hand in §8 of the plan before this
/// class existed - and working them out is what uncovered six inconsistencies in decisions the
/// decisions document had already closed, one of them a hole in the model. When one of these
/// fails, the first job is to find out which of the two is wrong, not to adjust the expected value
/// until it passes.
/// </summary>
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

    /// <summary>Component names in a given state, so an assertion reads like the table in §8.</summary>
    public IEnumerable<string> NodesIn(HealthState state) => Result.States
        .Where(s => s.Type == ElementType.Node && s.State == state)
        .Select(s => Graph.NodesById[s.Id].Name)
        .OrderBy(name => name, StringComparer.Ordinal);

    /// <summary>The containment border: the short list that explains why the architecture holds up (D6.1).</summary>
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
