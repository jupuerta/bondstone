using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// What the walk needs to know beyond capacity: which components a fault left dead, and which calls
/// expire. Two instances of these produce the <strong>two yields</strong> of plan técnico §5.4, and
/// mixing them brings the circularity back through another door - expiry depends on latency, which
/// depends on load, which would depend on a yield that knows about expiry, and the engine stops
/// terminating.
/// </summary>
/// <param name="Down">Components a fault left unable to answer.</param>
/// <param name="ExpiredEdges">Calls that cost more than their own timeout, so they fail every time (V3 D4.1).</param>
public sealed record YieldInputs(IReadOnlySet<Guid> Down, IReadOnlySet<Guid> ExpiredEdges)
{
    /// <summary>Capacity and nothing else. This is the one that feeds the retry amplification of pass 2.</summary>
    public static readonly YieldInputs CapacityOnly = new(new HashSet<Guid>(), new HashSet<Guid>());
}

/// <summary>
/// V4 DT.7 and plan técnico §5.4. The fraction of requests that can still complete, as a
/// <strong>deterministic bound</strong>: the minimum of the served fractions along the call tree.
///
/// The first version composed those fractions - multiplied them - and that was wrong twice over.
/// It gives 99,98 % on <c>gateway-microservices</c>, a number nobody can redo by hand and which
/// says the architecture is dead when what happens is that the database serves one call in six; and
/// multiplying fractions as if they were independent probabilities is stochastic reasoning smuggled
/// into a model V3 declared deterministic in its D1.2. The bound gives <strong>83 % and names
/// <c>Db</c></strong>, which is the sentence that fixes the problem.
///
/// It is the mirror image of <see cref="CallCost"/>: the same walk, the worst thing on the path
/// governing, with the operator turned around - latency takes the maximum, yield takes the minimum.
/// And like it, it is memoized, linear, and cuts cycles at the edge that closes them.
///
/// <strong>The bound is optimistic and says so.</strong> Two components at 50 % in series give
/// 50 %, not 25 %. In exchange the number can be redone in ten seconds and it points at what to
/// fix, which is what a challenge needs (D3.1).
/// </summary>
public sealed class RequestYield
{
    private readonly ArchitectureGraph _graph;
    private readonly LoadModel _load;
    private readonly YieldInputs _inputs;
    private readonly Dictionary<Guid, Served> _nodes = [];
    private readonly HashSet<Guid> _onPath = [];

    /// <param name="Fraction">Between 0 and 1.</param>
    /// <param name="Culprit">The element that set it, or null when nothing is holding anything back.</param>
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

    /// <summary>
    /// DT.7. What is left over from the worst entry point, and the component to blame. With no entry
    /// points there is nothing coming in, so nothing is affected.
    /// </summary>
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

    // -------------------------------------------------------------------------------------------

    private Served Visit(Guid nodeId)
    {
        if (_nodes.TryGetValue(nodeId, out var cached))
            return cached;

        // Reached a node already on the path: the edge that closes the cycle yields nothing back,
        // exactly as it costs nothing in CallCost (V3 D5.4).
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

    /// <summary>DT.5: a component over its capacity serves what it can and rejects the rest. It is not down - it is short.</summary>
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

        // V3 D4.7: nobody is waiting for an answer, so nothing downstream can make this request
        // fail. What does not happen is the work afterwards, and that is the backlog, not this.
        if (!edge.Synchronous)
            return Served.Whole;

        // V3 D4.5 read from the edge instead of from phase A, which is the third correction of plan
        // técnico §2: a pure-load scenario injects no fault, so phase A never runs and the fallback
        // of `resilient-async` would not contain. The guard of D4.2 stays - with no timeout there is
        // no error for any mechanism to catch, so `fallback` alone contains nothing.
        if (resilience.TimeoutSeconds is not null && resilience.Fallback)
            return Served.Whole;

        // A dead target and a call that expires every time are the same call, from here (V3 D4.1).
        if (_inputs.Down.Contains(edge.TargetNodeId) || _inputs.ExpiredEdges.Contains(edge.Id))
            return new Served(0m, FindingElement.Node(edge.TargetNodeId));

        return Visit(edge.TargetNodeId);
    }
}
