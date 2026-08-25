using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Simulation;

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

        foreach (var root in graph.RootNodes.OrderBy(n => n.Id))
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

        foreach (var root in _graph.RootNodes.OrderBy(n => n.Id))
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

    private Served Capacity(Node node)
    {
        var load = _load.LoadOf(node.Id);
        var capacity = _load.CapacityOf(node.Id);

        if (load <= 0m || capacity <= 0m || load <= capacity)
            return Served.Whole;

        return new Served(capacity / load, FindingElement.Node(node.Id));
    }

    private Served Through(Connection edge)
    {
        var resilience = _graph.ResilienceOf(edge);

        if (!edge.Synchronous)
            return Served.Whole;

        if (resilience.TimeoutSeconds is not null && resilience.Fallback)
            return Served.Whole;

        if (_inputs.Down.Contains(edge.TargetNodeId) || _inputs.ExpiredEdges.Contains(edge.Id))
            return new Served(0m, FindingElement.Node(edge.TargetNodeId));

        return Visit(edge.TargetNodeId);
    }
}
