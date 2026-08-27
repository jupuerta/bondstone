using arch_forge_backend.Application.Analysis;

namespace arch_forge_backend.Application.Simulation;

public sealed record SimulationScenario(IReadOnlyList<Fault> Faults, IReadOnlyList<string> Zones)
{
    public SimulationScenario(IReadOnlyList<Fault> faults) : this(faults, [])
    {
    }

    public static readonly SimulationScenario Empty = new([], []);

    public SimulationScenario Canonical() => this with
    {
        Faults = Faults.OrderBy(f => f.Target)
            .ThenBy(f => f.Id)
            .ThenBy(f => f.Kind, StringComparer.Ordinal)
            .ToList(),
        Zones = Zones.Distinct(StringComparer.Ordinal).OrderBy(z => z, StringComparer.Ordinal).ToList()
    };

    public bool HasDownFault =>
        Faults.Any(f => f.Kind == FaultKinds.Down);

    // The single door every scenario walks through on its way into the engine. A downed zone is
    // expanded here into one down fault per component placed in it, so nothing past this line knows
    // that zones exist; the URL and the challenge keep saying "zone a" and stay true when a
    // component is added to it later.
    public (SimulationScenario Applicable, IReadOnlyList<Fault> Discarded) Resolve(ArchitectureGraph graph)
    {
        var nodes = graph.NodesById.Keys.ToHashSet();
        var connections = graph.Connections.Select(c => c.Id).ToHashSet();

        var canonical = Canonical();

        var applicable = new List<Fault>();
        var discarded = new List<Fault>();
        var seen = new HashSet<(ElementType, Guid, string)>();

        foreach (var fault in canonical.Faults)
        {
            var exists = fault.Target == ElementType.Node
                ? nodes.Contains(fault.Id)
                : connections.Contains(fault.Id);

            if (!exists)
            {
                discarded.Add(fault);
                continue;
            }

            if (seen.Add((fault.Target, fault.Id, fault.Kind)))
                applicable.Add(fault);
        }

        foreach (var fault in Expand(graph, canonical.Zones))
        {
            if (seen.Add((fault.Target, fault.Id, fault.Kind)))
                applicable.Add(fault);
        }

        return (new SimulationScenario(applicable, canonical.Zones).Canonical(), discarded);
    }

    // An empty zone expands to nothing and is not announced as discarded: a discarded fault is a
    // Fault and a zone has no Guid to put in one. Naming a zone nobody is placed in is a scenario
    // that does nothing, not a broken link.
    private static IEnumerable<Fault> Expand(ArchitectureGraph graph, IReadOnlyList<string> zones)
    {
        if (zones.Count == 0)
            yield break;

        var down = zones.ToHashSet(StringComparer.Ordinal);

        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
        {
            if (NodeSettings.Zone(node) is { } zone && down.Contains(zone))
                yield return new Fault(ElementType.Node, node.Id, FaultKinds.Down);
        }
    }
}
