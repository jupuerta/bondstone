using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Analysis.Rules;

public static class EntryPoints
{
    public static IReadOnlyList<Node> PublicFacing(ArchitectureGraph graph) =>
        Frontier(graph).Where(n => NodeRoles.CanAuthenticate(n.Type))
            .OrderBy(n => n.Id)
            .ToList();

    public static IReadOnlyList<Node> Unguarded(ArchitectureGraph graph) =>
        Frontier(graph)
            .Where(n => NodeRoles.MustSitBehindADoor(n.Type) && !NodeRoles.IsSystemOfRecord(n.Type))
            .OrderBy(n => n.Id)
            .ToList();

    public static IReadOnlyList<Node> TrafficOrigins(ArchitectureGraph graph)
    {
        var clients = graph.Nodes.Where(n => NodeRoles.IsTrafficOrigin(n.Type)).ToList();
        if (clients.Count > 0)
            return clients;

        return TrafficRoots.Of(graph).Where(n => NodeRoles.ReceivesFromOutside(n.Type)).ToList();
    }

    public static IReadOnlyList<Node> Frontier(ArchitectureGraph graph) =>
        Frontier(graph, NodeRoles.CanAuthenticate);

    public static IReadOnlyList<Node> Frontier(ArchitectureGraph graph, Func<NodeType, bool> stopsAt)
    {
        var frontier = new List<Node>();
        var seen = new HashSet<Guid>();
        var pending = new Queue<Guid>(TrafficOrigins(graph).OrderBy(n => n.Id).Select(n => n.Id));

        while (pending.TryDequeue(out var nodeId))
        {
            if (!seen.Add(nodeId) || !graph.NodesById.TryGetValue(nodeId, out var node))
                continue;

            frontier.Add(node);

            if (stopsAt(node.Type))
                continue;

            foreach (var edge in graph.OutgoingBySource[nodeId].OrderBy(c => c.TargetNodeId).ThenBy(c => c.Id))
            {
                if (NodeRoles.IsTelemetryEdge(graph, edge))
                    continue;

                pending.Enqueue(edge.TargetNodeId);
            }
        }

        return frontier;
    }

    public static bool Reaches(ArchitectureGraph graph, Guid from, Func<NodeType, bool> matches)
    {
        var seen = new HashSet<Guid> { from };
        var pending = new Queue<Guid>([from]);

        while (pending.TryDequeue(out var nodeId))
        {
            foreach (var edge in graph.OutgoingBySource[nodeId])
            {
                if (NodeRoles.IsTelemetryEdge(graph, edge) || !seen.Add(edge.TargetNodeId))
                    continue;

                if (graph.NodesById.TryGetValue(edge.TargetNodeId, out var target) && matches(target.Type))
                    return true;

                pending.Enqueue(edge.TargetNodeId);
            }
        }

        return false;
    }
}
