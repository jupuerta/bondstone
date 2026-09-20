using bondstone_backend.Application.Analysis;
using bondstone_backend.Domain.Entities;

namespace bondstone_backend.Application.Simulation;

public sealed record YieldInputs(IReadOnlySet<Guid> Down, IReadOnlySet<Guid> ExpiredEdges)
{
    public static readonly YieldInputs CapacityOnly = new(new HashSet<Guid>(), new HashSet<Guid>());
}

public sealed class RequestYield
{
    private readonly ArchitectureGraph _graph;
    private readonly LoadModel _load;
    private readonly YieldInputs _inputs;
    private readonly Dictionary<Guid, Served> _nodes = [];
    private readonly HashSet<Guid> _onPath = [];

    private readonly record struct Served(decimal Fraction, FindingElement? Culprit)
    {
        public static readonly Served Whole = new(1m, null);
    }

    private RequestYield(ArchitectureGraph graph, LoadModel load, YieldInputs inputs)
    {
        _graph = graph;
        _load = load;
        _inputs = inputs;

        foreach (var root in graph.UserEntryNodes.OrderBy(n => n.Id))
            Visit(root.Id);

        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
            Visit(node.Id);
    }

    public static RequestYield Of(ArchitectureGraph graph, LoadModel load, YieldInputs inputs) =>
        new(graph, load, inputs);

    public decimal Of(Guid nodeId) => _nodes.GetValueOrDefault(nodeId, Served.Whole).Fraction;

    public (decimal Affected, FindingElement? Bottleneck) Impact()
    {
        var worst = Served.Whole;

        // Only what somebody is waiting for: what a clock or a worker sets off is work affected, and
        // it is not this number (QA 30-08, §10.3 point 10).
        foreach (var root in _graph.UserEntryNodes.OrderBy(n => n.Id))
        {
            var served = _nodes.GetValueOrDefault(root.Id, Served.Whole);
            if (served.Fraction < worst.Fraction)
                worst = served;
        }

        return (Math.Round(1m - worst.Fraction, 4), worst.Culprit);
    }


    private Served Visit(Guid nodeId)
    {
        if (_nodes.TryGetValue(nodeId, out var cached))
            return cached;

        if (!_onPath.Add(nodeId))
            return Served.Whole;

        var node = _graph.NodesById[nodeId];

        // A door whose every call says «I do not wait», with no broker holding the work, serves
        // nothing: the caller outside gets no answer, whatever happens behind (QA 30-08, §10.3
        // point 7). Without this, the drawing that unticks every box came out serving 100 %.
        if (Analysis.Rules.ImplausibleDependencyRule.WaitsForNobody(_graph, node))
        {
            _onPath.Remove(nodeId);
            _nodes[nodeId] = new Served(0m, FindingElement.Node(nodeId));
            return _nodes[nodeId];
        }

        var worst = Capacity(node);

        foreach (var edge in _graph.OutgoingBySource[nodeId].OrderBy(c => c.TargetNodeId).ThenBy(c => c.Id))
        {
            var served = Through(edge);
            if (served.Fraction < worst.Fraction)
                worst = served;
        }

        _onPath.Remove(nodeId);
        _nodes[nodeId] = worst;
        return worst;
    }

    // Utilization and not load over capacity: for a store the binding side may be the writes, which
    // the read replicas do not absorb (QA 30-08, 7.4), and the load model is the one that knows.
    private Served Capacity(Node node)
    {
        var utilization = _load.UtilizationOf(node.Id);

        if (utilization <= 1m)
            return Served.Whole;

        return new Served(1m / utilization, FindingElement.Node(node.Id));
    }

    private Served Through(Connection edge)
    {
        var resilience = _graph.EffectiveResilienceOf(edge);

        if (!edge.Synchronous)
            return Served.Whole;

        // A fallback serves the request only when there is something to answer with — not a router's
        // and not a declared write's: a payment that is not made cannot be approximated (§3.5, §7.4).
        // And only when it arrives in time: the alternative answer comes after the timeout expires, so
        // one behind a 60 s timeout reaches nobody — the same threshold the rule calls a wait and not
        // a limit, and the lesson `nobody-noticed` teaches (QA 30-08, §10.3 point 11).
        if (resilience.TimeoutSeconds is { } timeout && _graph.BelievesFallback(edge))
            return LatencyModel.TimeoutMs(timeout) < LatencyModel.UnboundedWait
                ? Served.Whole
                : new Served(0m, FindingElement.Connection(edge.Id));

        if (_inputs.Down.Contains(edge.TargetNodeId) || _inputs.ExpiredEdges.Contains(edge.Id))
            return new Served(0m, FindingElement.Node(edge.TargetNodeId));

        return Visit(edge.TargetNodeId);
    }
}
