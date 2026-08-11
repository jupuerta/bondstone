using arch_forge_backend.Application.Analysis;

namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// V3 D2.1. A canonical list of faults, and nothing else.
///
/// There is no cap on simultaneous faults and composing them is free: the lattice of D1.3 is
/// monotone, so the resulting state is the maximum and <em>does not depend on the order the faults
/// are applied in</em>. That is the first practical dividend of having chosen monotonicity in D1.5.
///
/// The empty scenario is valid and returns a result: zero faults means everything Healthy and the
/// metrics at rest. It is not a 400 - it is what the model says, and it is what the screen needs
/// when you enter Simulate mode before choosing anything.
/// </summary>
public sealed record SimulationScenario(IReadOnlyList<Fault> Faults)
{
    public static readonly SimulationScenario Empty = new([]);

    /// <summary>
    /// Ordered by <c>(Target, Id, Kind)</c> before applying and before serializing, so the same
    /// experiment always produces the same link and two links whose faults were written in a
    /// different order are the same experiment.
    /// </summary>
    public SimulationScenario Canonical() => new(
        Faults.OrderBy(f => f.Target)
            .ThenBy(f => f.Id)
            .ThenBy(f => f.Kind, StringComparer.Ordinal)
            .ToList());

    public bool HasDownFault =>
        Faults.Any(f => f.Kind == FaultKinds.Down);

    /// <summary>
    /// Faults pointing at elements that are no longer in the graph. A link shared before the
    /// architecture was edited must still open (D3.6), so these are dropped rather than rejected -
    /// but they are dropped out loud, because an experiment that lost half its faults and says
    /// nothing is worse than one that does not load.
    /// </summary>
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
