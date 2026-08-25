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
/// V17 D1. What the call that reached a hop did. The three cases <see cref="CallCost.Compute"/>
/// already distinguishes, named so a bar can be drawn without re-deriving them from a pair of
/// millisecond figures.
/// </summary>
public enum HopOutcome
{
    /// <summary>It answered, and the hop is worth what it did: hop, processing, queueing.</summary>
    Completed,

    /// <summary>
    /// D4.1. It costs more than its own timeout, so it expires every time and the caller is charged
    /// the timeout instead of the work. The work downstream does not get to happen, which is why the
    /// path ends here.
    /// </summary>
    Expired,

    /// <summary>
    /// The target is down, or the call was severed. From the caller's side those are the same call
    /// that never comes back, so they are the same bar.
    /// </summary>
    Unreachable
}

/// <summary>
/// V17 D1. One step of the critical path: the call that got here, the component it reached, and
/// <strong>what each of those cost</strong> - which is the whole proposal. The panel has printed
/// <c>120 ms → 6 900 ms</c> since V3 and the composition of both figures existed inside
/// <see cref="CallCost.Visit"/> for the length of one statement: <c>Math.Max</c> keeps the number
/// and throws away the argument.
///
/// <strong>The parts add up, on purpose.</strong>
/// <c><see cref="TotalMs"/> = <see cref="NetworkMs"/> + <see cref="ProcessingMs"/> +
/// <see cref="QueueingMs"/> + <see cref="InjectedMs"/></c> when the call completed, and
/// <c><see cref="TotalMs"/> = <see cref="CallMs"/></c> when it did not - the four are 0 there,
/// because a failed call costs its timeout and not the hop plus the subtree. A drawing whose
/// segments do not reconstruct its own bar is a drawing that lies, so the arithmetic is closed here
/// rather than left to the client.
/// </summary>
/// <param name="NodeId">The component this step reached.</param>
/// <param name="ConnectionId">
/// The call that reached it, or null on the first hop: the entry point is where the request starts,
/// not somewhere it was called from.
/// </param>
/// <param name="Outcome">What that call did - see <see cref="HopOutcome"/>. Always <see cref="HopOutcome.Completed"/> on the entry point.</param>
/// <param name="ProcessingMs">
/// <c>LatencyModel.Processing(type)</c>, the figure <c>/model</c> publishes in the abstract, here
/// applied to a box that was drawn. 0 when the call never got to be served.
/// </param>
/// <param name="QueueingMs">
/// DT.6. What the component adds <em>for being full</em>: the difference between its processing time
/// under the queueing factor and its processing time at rest. Today this only ever surfaces as a
/// consequence in a finding; it is the third segment of the bar and the reason a cascade drawn over
/// a saturated architecture looks nothing like the same drawing at rest.
/// </param>
/// <param name="QueueingFactor">
/// The <c>×ρ</c> of <see cref="CapacityModel.QueueingFactor"/> that produced
/// <paramref name="QueueingMs"/>, rounded to two decimals for the sentence beside the bar. The two
/// millisecond figures are the truth; do not re-derive one from the other.
/// </param>
/// <param name="NetworkMs">
/// <c>LatencyModel.Hop(protocol)</c> - Sql 2, Amqp 3, Http 5. 0 on the entry point and on a call
/// that failed.
/// </param>
/// <param name="InjectedMs">
/// What the experiment added at this step: the latency injected on the call plus the latency
/// injected on the component. 0 in every scenario that injects none, which is most of them.
/// </param>
/// <param name="TotalMs">What this step adds to the path.</param>
/// <param name="CumulativeMs">
/// The running total including this step. The last hop's is the whole path, and it is the same
/// number <see cref="SimulationMetrics.FaultedLatencyMs"/> reports - by construction, not by
/// agreement.
/// </param>
/// <param name="CallMs">
/// <c>EdgeCost.LatencyMs</c>: what the call cost the caller, subtree included. 0 on the entry point.
/// On the critical path it is exactly what is left to pay from here down, so it is also the check
/// that the reconstruction followed the walk.
/// </param>
/// <param name="RawCallMs">
/// <c>EdgeCost.RawLatencyMs</c>: what the call would have cost had it been allowed to finish. It
/// differs from <paramref name="CallMs"/> only when the call expires, and it is the number the
/// product's own sentence already quotes - <em>"the call to Shipping takes 8 s and the timeout is
/// 5 s"</em>. Null when there is nothing to annotate, so the bar paints what was charged and
/// annotates the raw figure rather than choosing between them (Anexo A, decision 1).
/// </param>
/// <param name="TimeoutMs">
/// The timeout declared on the call, in milliseconds, or null when none is. Null on a hop that did
/// not complete is <c>UnboundedWait</c>: the call never returns, the retries never happen, and the
/// 30 s bar has to be marked as what it is instead of flattening the rest of the cascade (Anexo A,
/// decision 3).
/// </param>
/// <param name="Retries">
/// What the call retries, as declared. It is what turns a failed bar into a sentence -
/// <c>5 s × (2 + 1)</c> - and D4.3's lesson is that it buys nobody anything except more load.
/// </param>
public sealed record LatencyHop(
    Guid NodeId,
    Guid? ConnectionId,
    HopOutcome Outcome,
    int ProcessingMs,
    int QueueingMs,
    decimal QueueingFactor,
    int NetworkMs,
    int InjectedMs,
    int TotalMs,
    int CumulativeMs,
    int CallMs,
    int? RawCallMs,
    int? TimeoutMs,
    int Retries);

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

    /// <summary>
    /// V17 D1. Which outgoing call won a node's <c>Math.Max</c> - one dictionary more, the same size
    /// as the two above, and the only thing the critical path needed that did not already exist.
    /// A node is absent when no call of its won with a cost strictly over zero, and that is where a
    /// path ends: see <see cref="CriticalPathFrom"/>.
    /// </summary>
    private readonly Dictionary<Guid, Connection> _worstEdge = [];
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

    /// <summary>
    /// V17 D1. The walk that produced <see cref="WorstFrom"/>, told back as a list of steps: the
    /// entry point that costs the most, and from there the call that won each node's maximum until
    /// nothing wins.
    ///
    /// <strong>It enumerates one path, never all of them.</strong> That distinction is the whole of
    /// V2's redesign - V1's <c>FindLongestChain</c> was exponential - and it survives here because
    /// the path is not searched for: it is read off the winners the linear walk already chose. The
    /// cost is one dictionary lookup per hop.
    ///
    /// <strong>Where it stops</strong>, and none of the three is a special case (Anexo A,
    /// decision 2):
    /// <list type="bullet">
    ///   <item>nothing costs anything from here on - a leaf, or a node all of whose calls are
    ///     asynchronous, or one whose only remaining call closes a cycle: all of those are 0, and
    ///     zero never wins;</item>
    ///   <item>the winning call failed - it expired or its target is unreachable - and a failed call
    ///     costs its timeout, not the hop plus the subtree, so there <em>is</em> no subtree to walk
    ///     into;</item>
    ///   <item>the guard below, which cannot fire. An edge back into the path the walk is currently
    ///     on is cut at 0 and therefore never a winner, so following winners cannot revisit a node.
    ///     It is here for the same reason phase A's round limit is: the proof is a paragraph, and
    ///     the loop is one line.</item>
    /// </list>
    ///
    /// Empty when there are no entry points left standing, which is exactly when
    /// <see cref="SimulationMetrics.FaultedLatencyMs"/> is null: no path to measure is a different
    /// statement from a path that costs zero (plan técnico §2).
    /// </summary>
    public IReadOnlyList<LatencyHop> CriticalPathFrom(IEnumerable<Node> roots)
    {
        Node? entry = null;
        var worst = -1;

        // Strictly greater over the id order, so the entry point is the same one WorstFrom's
        // Math.Max settles on and two roots tied on cost always resolve the same way (D5.6).
        foreach (var root in roots.OrderBy(n => n.Id))
        {
            var cost = Of(root.Id).LatencyMs;
            if (cost > worst)
            {
                worst = cost;
                entry = root;
            }
        }

        if (entry is null)
            return [];

        var hops = new List<LatencyHop>();
        var seen = new HashSet<Guid>();
        var cumulative = 0;
        var node = entry;
        Connection? incoming = null;

        while (seen.Add(node.Id))
        {
            var hop = HopInto(node, incoming, cumulative);
            hops.Add(hop);
            cumulative = hop.CumulativeMs;

            // A failed call is where the path ends: the work behind it never happens.
            if (hop.Outcome != HopOutcome.Completed)
                break;

            if (!_worstEdge.TryGetValue(node.Id, out incoming))
                break;

            node = _graph.NodesById[incoming.TargetNodeId];
        }

        return hops;
    }

    /// <summary>
    /// One step, priced with the same three lines <see cref="Compute"/> and <see cref="Visit"/>
    /// price it with - <c>hop + injected + processing × ρ</c> - taken apart into the segments the
    /// cascade draws instead of added up into the one number the walk needs.
    /// </summary>
    private LatencyHop HopInto(Node node, Connection? incoming, int cumulative)
    {
        var resilience = incoming is null ? null : _graph.ResilienceOf(incoming);
        var timeoutMs = resilience?.TimeoutSeconds is { } seconds ? LatencyModel.TimeoutMs(seconds) : (int?)null;
        var call = incoming is null ? null : Of(incoming);

        var outcome = incoming is null ? HopOutcome.Completed : OutcomeOf(incoming, call!);

        if (outcome != HopOutcome.Completed)
        {
            // What was charged is the whole of it, and the component behind the call contributes
            // nothing because it was never asked. The raw figure travels beside it, not instead of
            // it, so the bar can paint one and annotate the other.
            return new LatencyHop(
                node.Id,
                incoming!.Id,
                outcome,
                ProcessingMs: 0,
                QueueingMs: 0,
                QueueingFactor: 1m,
                NetworkMs: 0,
                InjectedMs: 0,
                TotalMs: call!.LatencyMs,
                CumulativeMs: cumulative + call.LatencyMs,
                CallMs: call.LatencyMs,
                RawCallMs: outcome == HopOutcome.Expired ? call.RawLatencyMs : null,
                TimeoutMs: timeoutMs,
                Retries: resilience?.Retries ?? 0);
        }

        var basis = LatencyModel.Processing(node.Type);
        var charged = _inputs.ProcessingOf(node);
        var network = incoming is null ? 0 : LatencyModel.Hop(incoming.Protocol);
        var injected = _inputs.LatencyOf(FindingElement.Node(node.Id))
                       + (incoming is null ? 0 : _inputs.LatencyOf(FindingElement.Connection(incoming.Id)));

        var total = network + charged + injected;

        return new LatencyHop(
            node.Id,
            incoming?.Id,
            HopOutcome.Completed,
            ProcessingMs: basis,
            QueueingMs: charged - basis,
            QueueingFactor: Math.Round(_inputs.QueueingFactors.GetValueOrDefault(node.Id, 1m), 2),
            NetworkMs: network,
            InjectedMs: injected,
            TotalMs: total,
            CumulativeMs: cumulative + total,
            CallMs: call?.LatencyMs ?? 0,
            RawCallMs: null,
            TimeoutMs: timeoutMs,
            Retries: resilience?.Retries ?? 0);
    }

    /// <summary>
    /// The same three branches <see cref="Compute"/> takes, asked after the fact. Severed and Down
    /// are one answer because they are one call from the caller's side, and expiry is read off the
    /// flag the walk already stored rather than recomputed.
    /// </summary>
    private HopOutcome OutcomeOf(Connection edge, EdgeCost cost)
    {
        if (_inputs.SeveredConnections.Contains(edge.Id) || _inputs.StateOf(edge.TargetNodeId) == HealthState.Down)
            return HopOutcome.Unreachable;

        return cost.Expires ? HopOutcome.Expired : HopOutcome.Completed;
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

            // V17 D1. The argument of the maximum, kept. Strictly greater, so it agrees with
            // Math.Max on ties over an already ordered list - and so that nothing costing zero can
            // ever win: an asynchronous call is 0 by definition (D4.7) and the edge that closes a
            // cycle is cut at 0 (D5.4), which is what makes the reconstructed path finite by
            // construction rather than by a special case.
            if (cost.LatencyMs > worstEdge)
            {
                worstEdge = cost.LatencyMs;
                _worstEdge[nodeId] = edge;
            }

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
