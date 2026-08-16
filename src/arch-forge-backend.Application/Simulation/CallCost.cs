using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Configuration.Schemas;
using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// What the walk knows about one component: what a call into it costs, and how many requests come
/// out of it for each one that goes in.
/// </summary>
public sealed record PathCost(int LatencyMs, decimal Amplification)
{
    public static readonly PathCost Free = new(0, 1m);
}

/// <param name="LatencyMs">What the call costs the caller.</param>
/// <param name="RawLatencyMs">
/// What the call would have cost had it been allowed to finish. It differs from
/// <paramref name="LatencyMs"/> only when the call expires, and it is the number the explanation
/// quotes: "the call to 'Shipping' takes 8 s and the timeout is 5 s".
/// </param>
/// <param name="Expires">
/// The call costs more than its timeout, so it expires every time. This is the flag phase A reads
/// to turn injected latency into a change of state (D4.1).
/// </param>
/// <param name="CycleCut">This edge closes a cycle and was cut at cost 0 (D5.4).</param>
public sealed record EdgeCost(int LatencyMs, int RawLatencyMs, decimal Amplification, bool Expires, bool CycleCut);

/// <summary>
/// What the model looks like while the walk runs. Everything the cost of a call depends on besides
/// the graph itself.
/// </summary>
/// <param name="SeveredConnections">Edges with an injected <c>down</c>: a partition, target alive but out of reach.</param>
public sealed record CostInputs(
    IReadOnlyDictionary<Guid, HealthState> NodeStates,
    IReadOnlyDictionary<FindingElement, int> InjectedLatencyMs,
    IReadOnlySet<Guid> SeveredConnections)
{
    /// <summary>No scenario at all: what the architecture costs when nothing is broken.</summary>
    public static readonly CostInputs Nominal = new(
        new Dictionary<Guid, HealthState>(),
        new Dictionary<FindingElement, int>(),
        new HashSet<Guid>());

    /// <summary>
    /// V4 DT.6. What each component's processing time is multiplied by, because of how close it is
    /// to its own capacity. <strong>An absent entry is 1</strong>, so an architecture with no
    /// declared load costs exactly what it cost in V3 - which is what makes the five reference
    /// scenarios survive the arrival of a whole new axis untouched.
    /// </summary>
    public IReadOnlyDictionary<Guid, decimal> QueueingFactors { get; init; } =
        new Dictionary<Guid, decimal>();

    public int ProcessingOf(Node node)
    {
        var factor = QueueingFactors.GetValueOrDefault(node.Id, 1m);
        var basis = LatencyModel.Processing(node.Type);

        return factor == 1m
            ? basis
            : (int)Math.Round(basis * factor, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Nodes with a <c>down</c> injected into them: genuinely gone, so they generate no work at
    /// all. Different from a node that is Down because its dependency fell - that one still runs
    /// and still hammers whatever it calls, which is what makes the ×12 of §8.5 real.
    /// </summary>
    public IReadOnlySet<Guid> DeadNodes { get; init; } = new HashSet<Guid>();

    /// <summary>
    /// Whether a failed call charges its timeout to the caller. True for the two passes that
    /// measure latency; false for the probe that decides <em>which</em> calls blow their timeout,
    /// where only time spent doing real work counts (see <see cref="CallCost"/>).
    /// </summary>
    public bool ChargeFailedCalls { get; init; } = true;

    public HealthState StateOf(Guid nodeId) =>
        NodeStates.TryGetValue(nodeId, out var state) ? state : HealthState.Healthy;

    public int LatencyOf(FindingElement element) =>
        InjectedLatencyMs.TryGetValue(element, out var ms) ? ms : 0;
}

/// <summary>
/// V3 D3.1 and D5.4, the arithmetic of plan técnico §5.2:
///
/// <code>
/// cost(node) = processing(type) + max over synchronous edges of cost(edge)
///
/// cost(edge) =
///     target healthy      hop(protocol) + cost(target) + injected latency
///     over the timeout    timeout × (retries + 1)      ← and the target counts as down
///     target down
///       circuit breaker   ~0
///       with timeout      timeout × (retries + 1)
///       without timeout   UnboundedWait
///     asynchronous        0
/// </code>
///
/// <strong>A failed call costs its timeout, not the hop plus the subtree.</strong> The work
/// afterwards does not get to happen.
///
/// The walk is memoized and linear, and <em>no path is ever enumerated</em>: that is the lesson of
/// V1, whose <c>FindLongestChain</c> was exponential, and V2 had to redesign it. The new problem is
/// that the synchronous subgraph <em>can</em> have cycles - <c>SyncDependencyChainRule</c> may
/// assume acyclicity because it excludes the edges CYCLE marked, and the simulator cannot, because
/// a synchronous cycle is precisely how a fault propagates.
/// </summary>
public sealed class CallCost
{
    private readonly ArchitectureGraph _graph;
    private readonly CostInputs _inputs;
    private readonly Dictionary<Guid, PathCost> _nodes = [];
    private readonly Dictionary<Guid, EdgeCost> _edges = [];
    private readonly HashSet<Guid> _onPath = [];

    private CallCost(ArchitectureGraph graph, CostInputs inputs)
    {
        _graph = graph;
        _inputs = inputs;

        // Entry points first, because the cost that matters is the one whoever comes in pays - and
        // because inside a cycle the memoized cost depends on where the walk came in (D5.4). Both
        // loops go in id order and never in input order: shuffling the lists has to produce the
        // same result, byte for byte (D5.6).
        foreach (var root in graph.RootNodes.OrderBy(n => n.Id))
            Visit(root.Id);

        // Everything the roots could not reach still needs a cost: phase A asks every edge whether
        // its call expires, including edges in a component nothing points into.
        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
            Visit(node.Id);
    }

    public static CallCost Of(ArchitectureGraph graph, CostInputs inputs) => new(graph, inputs);

    public PathCost Of(Guid nodeId) => _nodes.TryGetValue(nodeId, out var cost) ? cost : PathCost.Free;

    public EdgeCost Of(Connection edge) =>
        _edges.TryGetValue(edge.Id, out var cost) ? cost : new EdgeCost(0, 0, 1m, false, false);

    /// <summary>
    /// The worst cost seen from the entry points - "the latency that matters is the one whoever
    /// comes in sees" (D3.1). Null when no entry point is left standing, which is not the same as
    /// zero: a 0 would read as "instant" in the middle of a total outage (plan técnico §2).
    /// </summary>
    public int? WorstFrom(IEnumerable<Node> roots)
    {
        int? worst = null;

        foreach (var root in roots)
            worst = Math.Max(worst ?? 0, Of(root.Id).LatencyMs);

        return worst;
    }

    public decimal WorstAmplificationFrom(IEnumerable<Node> roots)
    {
        var worst = 1m;

        foreach (var root in roots)
            worst = Math.Max(worst, Of(root.Id).Amplification);

        return worst;
    }

    private PathCost Visit(Guid nodeId)
    {
        if (_nodes.TryGetValue(nodeId, out var cached))
            return cached;

        // Reached a node already on the path: this is the edge that closes the cycle. Returning
        // without memoizing is what keeps the cut local to this walk - the node's own cost is still
        // being computed further up the stack.
        if (!_onPath.Add(nodeId))
            return PathCost.Free;

        var node = _graph.NodesById[nodeId];
        var latency = _inputs.ProcessingOf(node) + _inputs.LatencyOf(FindingElement.Node(nodeId));
        var worstEdge = 0;
        var amplification = 1m;

        // Ordered by (target, id), exactly as SyncDependencyChainRule already does, so which edge
        // closes a cycle is a property of the graph and not of the order it was written in.
        foreach (var edge in _graph.OutgoingBySource[nodeId].OrderBy(c => c.TargetNodeId).ThenBy(c => c.Id))
        {
            var cost = VisitEdge(edge);
            worstEdge = Math.Max(worstEdge, cost.LatencyMs);
            amplification = Math.Max(amplification, cost.Amplification);
        }

        _onPath.Remove(nodeId);

        var result = new PathCost(latency + worstEdge, amplification);
        _nodes[nodeId] = result;
        return result;
    }

    private EdgeCost VisitEdge(Connection edge)
    {
        var resilience = _graph.ResilienceOf(edge);
        var targetState = _inputs.StateOf(edge.TargetNodeId);
        var severed = _inputs.SeveredConnections.Contains(edge.Id);

        var cost = Compute(edge, resilience, targetState, severed);
        _edges[edge.Id] = cost;
        return cost;
    }

    private EdgeCost Compute(Connection edge, ResilienceConfiguration resilience, HealthState targetState, bool severed)
    {
        // D5.4: a cycle of synchronous calls has no finite cost in reality - it is infinite
        // recursion - so any number would be invented. Cutting at the edge that closes it inflates
        // nothing and keeps the walk linear. The user already heard about the cycle from CYCLE.
        if (_onPath.Contains(edge.TargetNodeId))
            return new EdgeCost(0, 0, 1m, false, true);

        // D4.7. The caller does not wait for an answer, so this call costs it nothing. What it can
        // still do is multiply requests: each redelivery fires the consumer's calls again.
        if (!edge.Synchronous)
        {
            var redeliveries = NodeSettings.QueueRetries(_graph.NodesById[edge.SourceNodeId]);
            var factor = redeliveries >= 1 && targetState != HealthState.Healthy ? redeliveries + 1 : 1;
            return new EdgeCost(0, 0, factor * Downstream(edge, severed), false, false);
        }

        var timeoutMs = resilience.TimeoutSeconds is { } seconds ? LatencyModel.TimeoutMs(seconds) : (int?)null;

        // From the caller's side, a partition and a dead target are the same call that never
        // comes back.
        if (severed || targetState == HealthState.Down)
        {
            var failed = _inputs.ChargeFailedCalls ? FailedLatency(resilience, timeoutMs) : 0;
            return new EdgeCost(
                failed,
                failed,
                FailedAmplification(resilience, timeoutMs) * Downstream(edge, severed),
                false,
                false);
        }

        var target = Visit(edge.TargetNodeId);
        var raw = LatencyModel.Hop(edge.Protocol)
                  + _inputs.LatencyOf(FindingElement.Connection(edge.Id))
                  + target.LatencyMs;

        // D4.1, and the reason timeouts exist: the simulator produces "it expires every time"
        // without simulating a single second - it is a comparison between two numbers. It also
        // gives away the cascading-timeout lesson for free, because a caller whose child expires
        // sees a cost big enough to blow its own timeout too.
        if (timeoutMs is { } bound && raw > bound)
        {
            return new EdgeCost(
                _inputs.ChargeFailedCalls ? FailedLatency(resilience, timeoutMs) : 0,
                raw,
                FailedAmplification(resilience, timeoutMs) * target.Amplification,
                Expires: true,
                CycleCut: false);
        }

        return new EdgeCost(raw, raw, target.Amplification, false, false);
    }

    /// <summary>
    /// D4.2 and D4.4 in three lines. Without a timeout the call never returns, so it is not "like
    /// a timeout but worse" - it is a different class of failure, and the retries never happen. The
    /// circuit breaker is the only thing that gives the caller its control back at once.
    /// </summary>
    private static int FailedLatency(ResilienceConfiguration resilience, int? timeoutMs)
    {
        if (timeoutMs is not { } bound)
            return LatencyModel.UnboundedWait;

        return resilience.CircuitBreaker ? 0 : bound * ((resilience.Retries ?? 0) + 1);
    }

    /// <summary>
    /// D4.3: retries save nobody, they only pile requests onto the component that is already in
    /// trouble - and this is computed <em>under the scenario</em>, never over the nominal graph.
    /// In <c>gateway-microservices</c> that is the difference between announcing a ×4 avalanche and
    /// reporting the ×1 the model actually produces, because that edge has no timeout and without a
    /// timeout the retries do not get to happen. The circuit breaker is the one mechanism that cuts
    /// the amplification at the root, which is what makes it the answer to Post 3 (D4.4).
    /// </summary>
    private static decimal FailedAmplification(ResilienceConfiguration resilience, int? timeoutMs) =>
        timeoutMs is null || resilience.CircuitBreaker ? 1m : (resilience.Retries ?? 0) + 1;

    /// <summary>
    /// What the dependency itself multiplies. A component that is Down because <em>its</em>
    /// dependency fell still executes and still hammers whatever it calls - that is exactly the
    /// ×12 of §8.5, where the gateway retries Orders three times and each attempt has Orders retry
    /// Shipping four. Only a component that was killed outright, or an unreachable one, generates
    /// nothing.
    /// </summary>
    private decimal Downstream(Connection edge, bool severed) =>
        severed || _inputs.DeadNodes.Contains(edge.TargetNodeId) || _onPath.Contains(edge.TargetNodeId)
            ? 1m
            : Visit(edge.TargetNodeId).Amplification;
}
