using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Analysis.Rules;

public static class TrafficRoots
{
    public static IReadOnlyList<Node> Of(ArchitectureGraph graph) =>
        graph.Nodes
            .Where(node => !NodeRoles.IsTelemetry(node.Type)
                           && !graph.IncomingByTarget[node.Id].Any(edge => !NodeRoles.IsTelemetryEdge(graph, edge)))
            .ToList();
}
