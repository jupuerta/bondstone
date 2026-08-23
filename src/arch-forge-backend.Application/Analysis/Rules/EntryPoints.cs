using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V4 D5.2. What "exposed to the outside" means, in one place, because the three Security rules
/// would otherwise each answer it slightly differently - and three subtly different notions of
/// "public" is exactly how a Security area stops being trustworthy.
///
/// The front door is not simply <see cref="ArchitectureGraph.RootNodes"/>: traffic entering through
/// a balancer reaches the Apis behind it without meeting anything that could check who is calling,
/// so those Apis are just as public as the balancer. What stops the walk is precisely a component
/// that <em>can</em> ask - an Api or a Gateway.
/// </summary>
public static class EntryPoints
{
    /// <summary>An Api or a Gateway reachable from outside without another one in front of it.</summary>
    public static IReadOnlyList<Node> PublicFacing(ArchitectureGraph graph) =>
        Frontier(graph).Where(n => n.Type is NodeType.Api or NodeType.Gateway)
            .OrderBy(n => n.Id)
            .ToList();

    /// <summary>
    /// Where outside traffic comes from - declared when it can be, inferred when it must be
    /// (QA B6 / §3.1).
    ///
    /// <strong>A drawn Client settles the question.</strong> With one on the canvas, "public" is
    /// exactly what hangs from it, and every other root is by declaration internal. Without one,
    /// the old inference stands - a root is a door - minus the roots that could never be one:
    /// a Worker polling its queue, a Scheduler firing jobs, telemetry reading what it watches
    /// (<see cref="NodeRoles.ActsFromInside"/>). Inferring "internet" from those is how a
    /// background worker's private database ended up flagged as open to the world.
    /// </summary>
    public static IReadOnlyList<Node> TrafficOrigins(ArchitectureGraph graph)
    {
        var clients = graph.Nodes.Where(n => NodeRoles.IsTrafficOrigin(n.Type)).ToList();
        if (clients.Count > 0)
            return clients;

        return graph.RootNodes.Where(n => !NodeRoles.ActsFromInside(n.Type)).ToList();
    }

    /// <summary>Everything traffic reaches before meeting anything that could authenticate it.</summary>
    public static IReadOnlyList<Node> Frontier(ArchitectureGraph graph)
    {
        var frontier = new List<Node>();
        var seen = new HashSet<Guid>();
        var pending = new Queue<Guid>(TrafficOrigins(graph).OrderBy(n => n.Id).Select(n => n.Id));

        while (pending.TryDequeue(out var nodeId))
        {
            if (!seen.Add(nodeId) || !graph.NodesById.TryGetValue(nodeId, out var node))
                continue;

            frontier.Add(node);

            // An Api or a Gateway is where the outside stops being the outside.
            if (node.Type is NodeType.Api or NodeType.Gateway)
                continue;

            foreach (var edge in graph.OutgoingBySource[nodeId].OrderBy(c => c.TargetNodeId).ThenBy(c => c.Id))
                pending.Enqueue(edge.TargetNodeId);
        }

        return frontier;
    }

    /// <summary>
    /// Whether anything whose type satisfies <paramref name="matches"/> hangs behind
    /// <paramref name="from"/>. A predicate and not a <see cref="NodeType"/> since QA B3: the
    /// question Security actually asks is "is there data back there", and that is
    /// <see cref="NodeRoles.IsSystemOfRecord"/> - two types today, and one place to grow.
    /// </summary>
    public static bool Reaches(ArchitectureGraph graph, Guid from, Func<NodeType, bool> matches)
    {
        var seen = new HashSet<Guid> { from };
        var pending = new Queue<Guid>([from]);

        while (pending.TryDequeue(out var nodeId))
        {
            foreach (var edge in graph.OutgoingBySource[nodeId])
            {
                if (!seen.Add(edge.TargetNodeId))
                    continue;

                if (graph.NodesById.TryGetValue(edge.TargetNodeId, out var target) && matches(target.Type))
                    return true;

                pending.Enqueue(edge.TargetNodeId);
            }
        }

        return false;
    }
}
