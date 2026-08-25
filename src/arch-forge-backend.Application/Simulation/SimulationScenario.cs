using arch_forge_backend.Application.Analysis;

namespace arch_forge_backend.Application.Simulation;

public sealed record SimulationScenario(IReadOnlyList<Fault> Faults)
{
    public static readonly SimulationScenario Empty = new([]);

    public SimulationScenario Canonical() => new(
        Faults.OrderBy(f => f.Target)
            .ThenBy(f => f.Id)
            .ThenBy(f => f.Kind, StringComparer.Ordinal)
            .ToList());

    public bool HasDownFault =>
        Faults.Any(f => f.Kind == FaultKinds.Down);

    public (SimulationScenario Applicable, IReadOnlyList<Fault> Discarded) Resolve(ArchitectureGraph graph)
    {
        var nodes = graph.NodesById.Keys.ToHashSet();
        var connections = graph.Connections.Select(c => c.Id).ToHashSet();

        var applicable = new List<Fault>();
        var discarded = new List<Fault>();

        foreach (var fault in Canonical().Faults)
        {
            var exists = fault.Target == ElementType.Node
                ? nodes.Contains(fault.Id)
                : connections.Contains(fault.Id);

            (exists ? applicable : discarded).Add(fault);
        }

        return (new SimulationScenario(applicable), discarded);
    }
}
