using System.Text.Json;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Tests.Simulation.Scenarios;

/// <summary>
/// The same four reference architectures of V2, now with traffic on them.
///
/// The load is injected here rather than written into the fixture files, and that is the point:
/// <strong>the four fixtures stay exactly as V3 left them</strong>, so the five scenarios of V3 keep
/// passing to the digit while these four say something new about the same architectures. Declared
/// load is a property of an architecture (DT.1), so "the same drawing under 300 rps" is a different
/// architecture, not a different metric.
///
/// The expected numbers were worked out by hand in §8 of the plan before this class existed, and
/// working them out is what uncovered nine inconsistencies in decisions the decisions document had
/// already closed. When one of these fails, the first job is to find out which of the two is wrong.
/// </summary>
public sealed class LoadScenario
{
    private LoadScenario(ArchitectureGraph graph, LoadModel load, SimulationResult result)
    {
        Graph = graph;
        Load = load;
        Result = result;
    }

    public ArchitectureGraph Graph { get; }
    public LoadModel Load { get; }
    public SimulationResult Result { get; }
    public SimulationMetrics Metrics => Result.Metrics;

    public static LoadScenario Run(string fixtureName, int rps, params Fault[] faults)
    {
        var (nodes, connections) = ReferenceArchitecture.Load(fixtureName);
        var graph = new ArchitectureGraph(nodes, connections);

        // On the entry points, which is the only place the field means anything (DT.1).
        foreach (var root in graph.RootNodes)
            Declare(root, rps);

        var loaded = new ArchitectureGraph(nodes, connections);

        return new LoadScenario(
            loaded,
            LoadModel.Of(loaded),
            new SimulationEngine().Run(loaded, new SimulationScenario(faults)));
    }

    public decimal Utilization(string name) => Load.UtilizationOf(Graph.NodeId(name));

    public decimal LoadRps(string name) => Load.LoadOf(Graph.NodeId(name));

    public ElementState Node(string name) =>
        Result.States.Single(s => s.Type == ElementType.Node && s.Id == Graph.NodeId(name));

    public IEnumerable<string> NodesIn(HealthState state) => Result.States
        .Where(s => s.Type == ElementType.Node && s.State == state)
        .Select(s => Graph.NodesById[s.Id].Name)
        .OrderBy(name => name, StringComparer.Ordinal);

    /// <summary>Merges the field into whatever configuration the fixture already carries.</summary>
    private static void Declare(Node node, int rps)
    {
        var properties = new Dictionary<string, JsonElement>();

        if (!string.IsNullOrWhiteSpace(node.Configuration))
        {
            foreach (var property in JsonDocument.Parse(node.Configuration).RootElement.EnumerateObject())
                properties[property.Name] = property.Value.Clone();
        }

        properties["requestsPerSecond"] = JsonDocument.Parse(rps.ToString()).RootElement.Clone();
        node.Configuration = JsonSerializer.Serialize(properties);
    }
}
