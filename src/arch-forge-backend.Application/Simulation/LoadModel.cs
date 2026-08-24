using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// V4 bloque DT. The axis V3 rejected in its D3.2 and D3.3, acquired because without it half of §12
/// cannot be evaluated: how much traffic each component receives, how much of it it can take, and
/// how much slower being close to the limit makes it.
///
/// <strong>Declared load is a property of the architecture, not of the scenario</strong> (DT.1).
/// The analyzer reads it too - <c>UNDERSIZED_COMPONENT</c> is static analysis, not an experiment -
/// and computing it before any fault is applied is what keeps the two axes composable: the state of
/// a component is the worst of what saturation says and what propagation says, and neither has to
/// know about the other.
///
/// <strong>Absent load is zero, and zero changes nothing.</strong> Utilisation 0, queueing factor 1,
/// the four nominal figures of V3 - 27 · 57 · 39 · 72 ms - to the digit. That is not a courtesy, it
/// is the property that lets V4 add an axis without invalidating V3's five reference scenarios.
///
/// <strong>Saturation sheds nothing (QA B9).</strong> A component over its capacity passes the
/// whole of its load on to whatever it calls: an Api at 200 % sends the database every request it
/// received, not the half it can serve. That is a decision, not an oversight, and it is the same
/// one the product makes everywhere else - <em>only a declared mechanism sheds</em>. A gateway's
/// rate limit rejects at the door (<see cref="NodeSettings.Admit"/>), a queue delivers what
/// its consumers can take and keeps the rest as backlog (<see cref="Deliver"/>), a cache or a CDN
/// absorb their hit ratio. Being saturated is none of those: a service with no limit in front of it
/// does not turn requests away, it takes them all in, slows down, and fails the ones it cannot
/// finish - after they have already hit its dependencies. Modelling it as a polite rejection would
/// make NO_RATE_LIMIT's lesson worthless (the limit would buy nothing a saturated Api did not do by
/// itself) and would cap the retry storm of <c>worst-case</c> at the first service's capacity,
/// which is exactly the storm not happening. <see cref="RequestYield"/> reads the same overload
/// with its own question - what fraction gets an answer - and answers <c>capacity / load</c>; the
/// two axes disagree on purpose, the way latency takes the maximum of a fan-out while load takes
/// the sum. The user-facing sentence of UNDERSIZED_COMPONENT says so in as many words.
/// </summary>
public sealed class LoadModel
{
    private readonly Dictionary<Guid, decimal> _nodeLoad = [];
    private readonly Dictionary<Guid, decimal> _nodeDemand = [];
    private readonly Dictionary<Guid, decimal> _edgeLoad = [];
    private readonly Dictionary<Guid, decimal> _capacity = [];
    private readonly HashSet<Guid> _backEdges;
    private readonly List<Guid> _order;
    private readonly ArchitectureGraph _graph;

    private LoadModel(ArchitectureGraph graph)
    {
        _graph = graph;
        (_order, _backEdges) = TopologicalOrder(graph);

        foreach (var node in graph.Nodes)
            _capacity[node.Id] = CapacityModel.PerInstance(node.Type) * NodeSettings.Instances(node);
    }

    /// <summary>
    /// DT.3, the two passes of plan técnico §6.2.
    ///
    /// The amplification of an edge depends on how badly its target is doing, and how badly its
    /// target is doing depends on the load the amplification produces. <strong>The loop is
    /// positive</strong> - more load, worse yield, more retries, more load - so a fixed point over
    /// it may not converge, and it does not converge for a real reason: that is a retry storm, and
    /// it is the whole of Post 3.
    ///
    /// It is cut at two passes and declared as what it is: a first-order bound. The same class of
    /// decision as cutting a cycle at the edge that closes it - a number that falls short and
    /// terminates beats an exact one that does not exist. And the difference between the two passes
    /// <em>is</em> the product: in <c>worst-case</c> at 400 rps the database goes from 2.000 to
    /// 3.450 rps with no fault injected at all.
    /// </summary>
    public static LoadModel Of(ArchitectureGraph graph)
    {
        var model = new LoadModel(graph);

        // Pass 1: no amplification. Retries do not fire while everything answers (V3 D4.3 - the
        // multiplier lives only in the failure branches of CallCost), so charging them here would
        // inflate a perfectly healthy architecture by ×3.
        model.Propagate(_ => 1m);

        var capacityYield = RequestYield.Of(graph, model, YieldInputs.CapacityOnly);

        // Pass 2: retries charge only over the fraction that fails.
        model.Propagate(edge =>
        {
            var resilience = graph.ResilienceOf(edge);
            var retries = resilience.Retries ?? 0;

            // V3 D4.2, the guard that makes the timeout the key to everything else: with no timeout
            // the call never returns, so the retry never happens.
            if (retries <= 0 || resilience.TimeoutSeconds is null)
                return 1m;

            return 1m + retries * (1m - capacityYield.Of(edge.TargetNodeId));
        });

        return model;
    }

    /// <summary>Requests per second arriving at a component.</summary>
    public decimal LoadOf(Guid nodeId) => _nodeLoad.GetValueOrDefault(nodeId);

    /// <summary>
    /// V14 §4.3. What wants to enter a node, before its rate limit turns anything away: declared
    /// plus arriving. <see cref="LoadOf"/> is what got in; the difference is what TRAFFIC_REJECTED
    /// has to say out loud, because a limit that sheds most of the declared demand had been making
    /// everything behind it look healthy - the limit converting "undersized" into "fine".
    /// </summary>
    public decimal DemandOf(Guid nodeId) => _nodeDemand.GetValueOrDefault(nodeId);

    public decimal CapacityOf(Guid nodeId) => _capacity.GetValueOrDefault(nodeId, 1m);

    /// <summary>DT.5. Zero when nothing was declared, which is the case for every architecture drawn before V4.</summary>
    public decimal UtilizationOf(Guid nodeId)
    {
        var capacity = CapacityOf(nodeId);
        return capacity <= 0m ? 0m : LoadOf(nodeId) / capacity;
    }

    /// <summary>DT.6. The multiplier on a component's processing time. Exactly 1 with no load.</summary>
    public decimal QueueingFactorOf(Guid nodeId) => CapacityModel.QueueingFactor(UtilizationOf(nodeId));

    public IReadOnlyDictionary<Guid, decimal> QueueingFactors =>
        _graph.Nodes.ToDictionary(n => n.Id, n => QueueingFactorOf(n.Id));

    /// <summary>True when nobody declared any traffic, which is the shortcut the whole of V3 relies on.</summary>
    public bool IsIdle => _nodeLoad.Values.All(load => load <= 0m);

    /// <summary>DT.7. What piles up in the queues because their consumers cannot keep up, in rps.</summary>
    public decimal BacklogRps { get; private set; }

    /// <summary>
    /// DT.7. Requests reaching the busiest component for each one that comes in the front door.
    /// <strong>Not</strong> V3's <c>RequestAmplification</c>, which is the worst path under a
    /// scenario: this one is the sum arriving at a node. On <c>worst-case</c> they read ×12 and ×24
    /// and both are true, which is why they do not share a name.
    /// </summary>
    public decimal LoadAmplification
    {
        get
        {
            var entering = _graph.RootNodes.Sum(n => NodeSettings.Admit(n, NodeSettings.DeclaredRps(n)));
            if (entering <= 0m)
                return 1m;

            var busiest = _graph.Nodes.Max(n => LoadOf(n.Id));
            return Math.Round(busiest / entering, 2);
        }
    }

    // -------------------------------------------------------------------------------------------

    private void Propagate(Func<Connection, decimal> amplification)
    {
        _nodeLoad.Clear();
        _nodeDemand.Clear();
        _edgeLoad.Clear();
        BacklogRps = 0m;

        foreach (var nodeId in _order)
        {
            var node = _graph.NodesById[nodeId];

            // Everything upstream is already final: that is what the topological order buys, and it
            // is why a Queue can cap and a LoadBalancer can split - neither operation is additive,
            // so neither can be done while pushing increments forward.
            var incoming = _graph.IncomingByTarget[nodeId]
                .Where(edge => !_backEdges.Contains(edge.Id))
                .Sum(edge => _edgeLoad.GetValueOrDefault(edge.Id));

            // The door admits at most its rate limit, whether the traffic was declared on it or
            // arrived from the Client in front of it (DT.3, QA §4). What wanted in is kept too
            // (V14 §4.3): the difference between the two is the rejected traffic.
            var demand = NodeSettings.DeclaredRps(node) + incoming;
            var load = NodeSettings.Admit(node, demand);
            _nodeDemand[nodeId] = demand;
            _nodeLoad[nodeId] = load;

            DistributeOutgoing(node, load, amplification);
        }
    }

    private void DistributeOutgoing(Node node, decimal load, Func<Connection, decimal> amplification)
    {
        var outgoing = _graph.OutgoingBySource[node.Id]
            .OrderBy(c => c.TargetNodeId)
            .ThenBy(c => c.Id)
            .ToList();

        if (outgoing.Count == 0)
            return;

        // Instrumentation is not traffic (QA §4, coverage): the wire to the telemetry carries no
        // requests and is not one of the consumers a queue or a balancer splits its work among.
        // Without this, wiring a queue to the telemetry - what NO_OBSERVABILITY asks for - halved
        // what its worker received.
        var fanOut = outgoing.Count(edge => !NodeRoles.IsTelemetryEdge(_graph, edge));

        foreach (var edge in outgoing)
        {
            if (NodeRoles.IsTelemetryEdge(_graph, edge))
            {
                _edgeLoad[edge.Id] = 0m;
                continue;
            }

            // DT.3: the edge that closes a cycle carries nothing. Same rule and same reason as V3
            // D5.4 - going round adds traffic on every lap and never settles - and it underreports
            // the load of the nodes inside the cycle, which is written down rather than hidden.
            // CYCLE already told the user there is one.
            if (_backEdges.Contains(edge.Id))
            {
                _edgeLoad[edge.Id] = 0m;
                continue;
            }

            _edgeLoad[edge.Id] = Basis(node, load, fanOut, edge) * amplification(edge);
        }
    }

    private decimal Basis(Node node, decimal load, int fanOut, Connection edge) => node.Type switch
    {
        // DT.3: a cache lets through only what it misses. This is what solves Challenge #004 -
        // a cache in front and the database's utilisation falls to a fifth.
        NodeType.Cache => load * (1m - NodeSettings.HitRatio(node)),

        // QA §3.2: the same absorption at the edge, with its own default. The origin behind a CDN
        // sees a tenth of the traffic, which is the whole lesson the type exists to teach.
        NodeType.Cdn => load * (1m - NodeSettings.HitRatio(node)),

        // QA §3.1: the declared users enter through however many doors they use, they do not
        // duplicate per door - the outside splits like a balancer, not like a fan-out of calls.
        NodeType.Client => load / fanOut,

        // Competing consumers: the work splits, and each consumer takes at most what it can chew.
        // What does not fit does not vanish - it is backlog, and a *rate* of growth needs no clock,
        // which is exactly why V3 could reject backlog and V4 can accept it.
        NodeType.Queue => Deliver(load / fanOut, edge),

        // QA V14 B5: a router routes, and NodeRoles.IsRouter says which two do. A Gateway's fan-out
        // is a routing table, not a list of calls it makes on every request - it sends each one
        // down *one* route exactly as a LoadBalancer sends it to *one* instance. Only the balancer
        // split until V14, and the gateway duplicating had two consequences worse than the number:
        // splitting a monolith into three relieved nothing, so HIGH_COUPLING's lesson was
        // contradicted by UNDERSIZED_COMPONENT's; and a second gateway for redundancy doubled what
        // the database received, so *adding redundancy lowered the grade*. Equal shares are as
        // arbitrary as any other split, but duplicating invents traffic, which is strictly worse.
        _ when NodeRoles.IsRouter(node.Type) => load / fanOut,

        // Everything else: a fan-out means all of those calls happen. The load sums them while the
        // latency takes the maximum, because they are parallel - each axis reads the same fan-out
        // with the question that is its own (plan técnico §2, corrección 7). And it is `load`,
        // not `min(load, capacity)`: saturation sheds nothing, only declared mechanisms do (QA B9,
        // class summary).
        _ => load
    };

    private decimal Deliver(decimal share, Connection edge)
    {
        var consumerCapacity = CapacityOf(edge.TargetNodeId);
        var delivered = Math.Min(share, consumerCapacity);

        BacklogRps += share - delivered;
        return delivered;
    }

    /// <summary>
    /// Depth-first from the entry points, marking the edges that close a cycle, and answering with
    /// the reverse postorder - every node after everything that feeds it. Roots first and then the
    /// rest, both in id order, so which edge closes a cycle is a property of the graph and not of
    /// the order it was written in (V3 D5.6).
    /// </summary>
    private static (List<Guid> Order, HashSet<Guid> BackEdges) TopologicalOrder(ArchitectureGraph graph)
    {
        const int OnStack = 1;
        const int Done = 2;

        var color = new Dictionary<Guid, int>();
        var backEdges = new HashSet<Guid>();
        var postorder = new List<Guid>();

        void Visit(Guid nodeId)
        {
            color[nodeId] = OnStack;

            foreach (var edge in graph.OutgoingBySource[nodeId].OrderBy(c => c.TargetNodeId).ThenBy(c => c.Id))
            {
                switch (color.GetValueOrDefault(edge.TargetNodeId))
                {
                    case OnStack:
                        backEdges.Add(edge.Id);
                        break;
                    case Done:
                        break;
                    default:
                        Visit(edge.TargetNodeId);
                        break;
                }
            }

            color[nodeId] = Done;
            postorder.Add(nodeId);
        }

        foreach (var root in graph.RootNodes.OrderBy(n => n.Id))
            if (!color.ContainsKey(root.Id))
                Visit(root.Id);

        // Anything the roots cannot reach still needs a place in the order: it carries no declared
        // load, but the walk has to cover every node for the utilisation of each to be defined.
        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
            if (!color.ContainsKey(node.Id))
                Visit(node.Id);

        postorder.Reverse();
        return (postorder, backEdges);
    }
}
