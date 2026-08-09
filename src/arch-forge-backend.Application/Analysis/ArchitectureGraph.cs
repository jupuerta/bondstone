using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Analysis;

public class ArchitectureGraph
{
    public ArchitectureGraph(IReadOnlyList<Node> nodes, IReadOnlyList<Connection> connections)
    {
        Nodes = nodes;
        Connections = connections;
    }

    public IReadOnlyList<Node> Nodes { get; }
    public IReadOnlyList<Connection> Connections { get; }

    // Extension point for future rules that need to read what another rule found
    // (e.g. a "bottleneck" rule checking for an existing SPOF). Unused by V1's two
    // rules, which are independent - see ArchForge_decisiones_arquitectura_V1_1.md §3.
    public List<ArchitectureFact> Facts { get; } = new();
}
