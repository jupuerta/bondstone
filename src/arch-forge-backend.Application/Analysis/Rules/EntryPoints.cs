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
/// that <em>can</em> ask - <see cref="NodeRoles.CanAuthenticate"/>, which since V14 B6 includes a
/// Service, because a BFF is a door too.
///
/// V14 B6. And which roots are doors is <see cref="NodeRoles.ReceivesFromOutside"/>'s answer, read
/// over <see cref="TrafficRoots"/> rather than the raw roots: the first so that a queue or a cache
/// at the top of a partial drawing is not the internet, the second so that a scrape arrow does not
/// take the Api out of the roots and with it out of Security's view entirely (the B1 bug, one
/// area over).
/// </summary>
public static class EntryPoints
{
    /// <summary>A door - anything that can authenticate - reachable from outside without another one in front of it.</summary>
    public static IReadOnlyList<Node> PublicFacing(ArchitectureGraph graph) =>
        Frontier(graph).Where(n => NodeRoles.CanAuthenticate(n.Type))
            .OrderBy(n => n.Id)
            .ToList();

    /// <summary>
    /// Where outside traffic comes from - declared when it can be, inferred when it must be
    /// (QA B6 / §3.1).
    ///
    /// <strong>A drawn Client settles the question.</strong> With one on the canvas, "public" is
    /// exactly what hangs from it, and every other root is by declaration internal. Without one,
    /// the inference runs - and since V14 B6 it runs the right way up. QA B6 said "a root is a
    /// door, except a Worker, a Scheduler or the telemetry", which left a queue, a cache, a service
    /// or a database at the top of a partial drawing standing in for the internet. It is now "a
    /// root is a door only if the outside could reach it" (<see cref="NodeRoles.ReceivesFromOutside"/>):
    /// a store or a broker at the root is a diagram that starts in the middle, not a breach.
    ///
    /// Over <see cref="TrafficRoots"/> and not <see cref="ArchitectureGraph.RootNodes"/>, for the
    /// reason V14 B1 gave Availability: <c>Obs → Api</c> is a scrape, not a call, and it must not
    /// take the Api out of the roots - which here meant out of Security's sight altogether, every
    /// door rule going quiet the moment the telemetry was wired the way NO_OBSERVABILITY asks.
    /// </summary>
    public static IReadOnlyList<Node> TrafficOrigins(ArchitectureGraph graph)
    {
        var clients = graph.Nodes.Where(n => NodeRoles.IsTrafficOrigin(n.Type)).ToList();
        if (clients.Count > 0)
            return clients;

        return TrafficRoots.Of(graph).Where(n => NodeRoles.ReceivesFromOutside(n.Type)).ToList();
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

            // A door is where the outside stops being the outside - and a door is whatever can
            // ask who is calling (V14 B6), not two types picked in V4.
            if (NodeRoles.CanAuthenticate(node.Type))
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
