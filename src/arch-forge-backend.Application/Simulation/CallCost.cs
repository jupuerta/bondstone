using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Configuration.Schemas;
using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Simulation;

public sealed record PathCost(int LatencyMs, decimal Amplification)
{
    public static readonly PathCost Free = new(0, 1m);
}

public sealed record EdgeCost(int LatencyMs, int RawLatencyMs, decimal Amplification, bool Expires, bool CycleCut);

public enum HopOutcome
{
    Completed,

    Expired,

    Unreachable
}

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

public sealed record CostInputs(
    IReadOnlyDictionary<Guid, HealthState> NodeStates,
    IReadOnlyDictionary<FindingElement, int> InjectedLatencyMs,
    IReadOnlySet<Guid> SeveredConnections)
{
    public static readonly CostInputs Nominal = new(
        new Dictionary<Guid, HealthState>(),
        new Dictionary<FindingElement, int>(),
        new HashSet<Guid>());

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

    public IReadOnlySet<Guid> DeadNodes { get; init; } = new HashSet<Guid>();

    public bool ChargeFailedCalls { get; init; } = true;

    public HealthState StateOf(Guid nodeId) =>
        NodeStates.TryGetValue(nodeId, out var state) ? state : HealthState.Healthy;

    public int LatencyOf(FindingElement element) =>
        InjectedLatencyMs.TryGetValue(element, out var ms) ? ms : 0;
}

public sealed class CallCost
{
    private readonly ArchitectureGraph _graph;
    private readonly CostInputs _inputs;
    private readonly Dictionary<Guid, PathCost> _nodes = [];
    private readonly Dictionary<Guid, EdgeCost> _edges = [];

    private readonly Dictionary<Guid, Connection> _worstEdge = [];
    private readonly HashSet<Guid> _onPath = [];

    private CallCost(ArchitectureGraph graph, CostInputs inputs)
    {
        _graph = graph;
        _inputs = inputs;

        foreach (var root in graph.RootNodes.OrderBy(n => n.Id))
            Visit(root.Id);

        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
            Visit(node.Id);
    }

    public static CallCost Of(ArchitectureGraph graph, CostInputs inputs) => new(graph, inputs);

    public PathCost Of(Guid nodeId) => _nodes.TryGetValue(nodeId, out var cost) ? cost : PathCost.Free;

    public EdgeCost Of(Connection edge) =>
        _edges.TryGetValue(edge.Id, out var cost) ? cost : new EdgeCost(0, 0, 1m, false, false);

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

    public IReadOnlyList<LatencyHop> CriticalPathFrom(IEnumerable<Node> roots)
    {
        Node? entry = null;
        var worst = -1;

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

            if (hop.Outcome != HopOutcome.Completed)
                break;

            if (!_worstEdge.TryGetValue(node.Id, out incoming))
                break;

            node = _graph.NodesById[incoming.TargetNodeId];
        }

        return hops;
    }

    private LatencyHop HopInto(Node node, Connection? incoming, int cumulative)
    {
        var resilience = incoming is null ? null : _graph.ResilienceOf(incoming);
        var timeoutMs = resilience?.TimeoutSeconds is { } seconds ? LatencyModel.TimeoutMs(seconds) : (int?)null;
        var call = incoming is null ? null : Of(incoming);

        var outcome = incoming is null ? HopOutcome.Completed : OutcomeOf(incoming, call!);

        if (outcome != HopOutcome.Completed)
        {
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

        if (!_onPath.Add(nodeId))
            return PathCost.Free;

        var node = _graph.NodesById[nodeId];
        var latency = _inputs.ProcessingOf(node) + _inputs.LatencyOf(FindingElement.Node(nodeId));
        var worstEdge = 0;
        var amplification = 1m;

        foreach (var edge in _graph.OutgoingBySource[nodeId].OrderBy(c => c.TargetNodeId).ThenBy(c => c.Id))
        {
            var cost = VisitEdge(edge);

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
        if (_onPath.Contains(edge.TargetNodeId))
            return new EdgeCost(0, 0, 1m, false, true);

        if (!edge.Synchronous)
        {
            var redeliveries = NodeSettings.QueueRetries(_graph.NodesById[edge.SourceNodeId]);
            var factor = redeliveries >= 1 && targetState != HealthState.Healthy ? redeliveries + 1 : 1;
            return new EdgeCost(0, 0, factor * Downstream(edge, severed), false, false);
        }

        var timeoutMs = resilience.TimeoutSeconds is { } seconds ? LatencyModel.TimeoutMs(seconds) : (int?)null;

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

    private static int FailedLatency(ResilienceConfiguration resilience, int? timeoutMs)
    {
        if (timeoutMs is not { } bound)
            return LatencyModel.UnboundedWait;

        return resilience.CircuitBreaker ? 0 : bound * ((resilience.Retries ?? 0) + 1);
    }

    private static decimal FailedAmplification(ResilienceConfiguration resilience, int? timeoutMs) =>
        timeoutMs is null || resilience.CircuitBreaker ? 1m : (resilience.Retries ?? 0) + 1;

    private decimal Downstream(Connection edge, bool severed) =>
        severed || _inputs.DeadNodes.Contains(edge.TargetNodeId) || _onPath.Contains(edge.TargetNodeId)
            ? 1m
            : Visit(edge.TargetNodeId).Amplification;
}
