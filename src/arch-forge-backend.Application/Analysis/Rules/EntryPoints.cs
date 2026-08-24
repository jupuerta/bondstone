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
///
/// V15 C1 + E5. Two more answers, and both are about what the walk finds <em>other</em> than a
/// door. A bucket it reaches only through an edge cache is the bucket the CDN serves, not a public
/// store - the same walk with one more stop (<see cref="Frontier(ArchitectureGraph, Func{NodeType, bool})"/>,
/// read by EXPOSED_DATABASE). And a box of yours it reaches that can neither ask who is calling nor
/// hand the request to something that can is the observation Security had no sentence for
/// (<see cref="Unguarded"/>): until V15 a drawing where <em>nothing</em> could authenticate left the
/// area without a score, which is silence on the one drawing where the question mattered most.
/// </summary>
public static class EntryPoints
{
    /// <summary>A door - anything that can authenticate - reachable from outside without another one in front of it.</summary>
    public static IReadOnlyList<Node> PublicFacing(ArchitectureGraph graph) =>
        Frontier(graph).Where(n => NodeRoles.CanAuthenticate(n.Type))
            .OrderBy(n => n.Id)
            .ToList();

    /// <summary>
    /// V15 E5. What the outside reaches with nowhere to check who is calling: the frontier minus
    /// the doors, which get the door's question through <see cref="PublicFacing"/>, and minus
    /// what is there to be reached - the outside itself, the way in, somebody else's box
    /// (<see cref="NodeRoles.MustSitBehindADoor"/>). The data is left out too, and not because
    /// the sentence is false of it: EXPOSED_DATABASE has said it under its own name since V4,
    /// and one node does not get the same lesson twice under two codes.
    ///
    /// The hole symmetric to B6. That one had roots that were not doors read as the internet;
    /// this one had the internet reaching things that were not doors and nobody saying so -
    /// <c>Client → Cdn → Worker</c> came out with Security <em>not applicable</em>, the area out of
    /// the average and not a finding on the panel.
    /// </summary>
    public static IReadOnlyList<Node> Unguarded(ArchitectureGraph graph) =>
        Frontier(graph)
            .Where(n => NodeRoles.MustSitBehindADoor(n.Type) && !NodeRoles.IsSystemOfRecord(n.Type))
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
    public static IReadOnlyList<Node> Frontier(ArchitectureGraph graph) =>
        Frontier(graph, NodeRoles.CanAuthenticate);

    /// <summary>
    /// V15 C1. The same walk, stopped by <paramref name="stopsAt"/> rather than by a door alone.
    /// The walk does not change - a door is where the outside stops being the outside, and the
    /// one-argument form is exactly that - but EXPOSED_DATABASE has a second question that is the
    /// same walk with one more stop: whether the outside reaches a bucket <em>without an edge cache
    /// in the way</em>. The node the walk stops at is in the answer and what hangs behind it is
    /// not, the same as for a door.
    ///
    /// <strong>V15 E5 (B9).</strong> The instrumentation is not a path. An edge cache logging to
    /// the telemetry and the telemetry scraping a worker is not the outside reaching the worker,
    /// and until V15 the walk followed that cable; with a finding waiting on the other end of it,
    /// following it would have cost a point for wiring the telemetry - the one thing the analyzer
    /// promises never to do (<see cref="NodeRoles.IsTelemetryEdge"/>).
    /// </summary>
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

            // A door is where the outside stops being the outside - and a door is whatever can
            // ask who is calling (V14 B6), not two types picked in V4.
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

    /// <summary>
    /// Whether anything whose type satisfies <paramref name="matches"/> hangs behind
    /// <paramref name="from"/>. A predicate and not a <see cref="NodeType"/> since QA B3: the
    /// question Security actually asks is "is there data back there", and that is
    /// <see cref="NodeRoles.IsSystemOfRecord"/> - two types today, and one place to grow.
    ///
    /// V15 E5 (B9). Not down an instrumentation cable: a door pushing its metrics to the
    /// telemetry, and the telemetry scraping a database, is not the door reaching the data.
    /// </summary>
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
