using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Simulation;

public sealed class LoadModel
{
    private readonly Dictionary<Guid, decimal> _nodeLoad = [];
    private readonly Dictionary<Guid, decimal> _nodeDemand = [];
    private readonly Dictionary<Guid, decimal> _edgeLoad = [];
    private readonly Dictionary<Guid, decimal> _capacity = [];
    private readonly Dictionary<Guid, decimal> _writeLoad = [];
    private readonly Dictionary<Guid, decimal> _writeCapacity = [];
    private readonly HashSet<Guid> _backEdges;
    private readonly List<Guid> _order;
    private readonly ArchitectureGraph _graph;
    private readonly decimal _demandScale;

    private LoadModel(ArchitectureGraph graph, decimal demandScale)
    {
        _graph = graph;
        _demandScale = demandScale;
        (_order, _backEdges) = TopologicalOrder(graph);

        foreach (var node in graph.Nodes)
        {
            _capacity[node.Id] = CapacityModel.PerInstance(node.Type) * NodeSettings.Instances(node);

            // A store's read replicas do not write: the declared writes have only the primary and
            // its full replicas to land on (QA 30-08, §4.7 and 7.4). What nobody declares counts as
            // a read, which is what it always counted as.
            if (NodeRoles.IsSystemOfRecord(node.Type))
                _writeCapacity[node.Id] = CapacityModel.PerInstance(node.Type) * NodeSettings.WriteInstances(node);
        }
    }

    public static LoadModel Of(ArchitectureGraph graph, decimal demandScale = 1m)
    {
        var model = new LoadModel(graph, demandScale);

        model.Propagate(_ => 1m);

        var capacityYield = RequestYield.Of(graph, model, YieldInputs.CapacityOnly);

        model.Propagate(edge =>
        {
            var resilience = graph.ResilienceOf(edge);
            var retries = resilience.Retries ?? 0;

            if (retries <= 0 || resilience.TimeoutSeconds is null)
                return 1m;

            return 1m + retries * (1m - capacityYield.Of(edge.TargetNodeId));
        });

        return model;
    }

    public decimal LoadOf(Guid nodeId) => _nodeLoad.GetValueOrDefault(nodeId);

    public decimal DemandOf(Guid nodeId) => _nodeDemand.GetValueOrDefault(nodeId);

    public decimal CapacityOf(Guid nodeId) => _capacity.GetValueOrDefault(nodeId, 1m);

    public decimal CallLoadOf(Guid connectionId) => _edgeLoad.GetValueOrDefault(connectionId);

    public decimal UtilizationOf(Guid nodeId)
    {
        var capacity = CapacityOf(nodeId);
        var utilization = capacity <= 0m ? 0m : LoadOf(nodeId) / capacity;

        return Math.Max(utilization, WriteUtilizationOf(nodeId));
    }

    public decimal WriteLoadOf(Guid nodeId) => _writeLoad.GetValueOrDefault(nodeId);

    public decimal WriteCapacityOf(Guid nodeId) => _writeCapacity.GetValueOrDefault(nodeId);

    // True when it is the declared writes, and not the whole load, that put the store over.
    public bool WritesBind(Guid nodeId)
    {
        var capacity = CapacityOf(nodeId);
        var whole = capacity <= 0m ? 0m : LoadOf(nodeId) / capacity;

        return WriteUtilizationOf(nodeId) > whole;
    }

    // The pair a sentence should quote: the writes against what takes them when those bind, the
    // whole load against the whole capacity otherwise.
    public (decimal Load, decimal Capacity) BindingOf(Guid nodeId) =>
        WritesBind(nodeId) ? (WriteLoadOf(nodeId), WriteCapacityOf(nodeId)) : (LoadOf(nodeId), CapacityOf(nodeId));

    private decimal WriteUtilizationOf(Guid nodeId)
    {
        var capacity = WriteCapacityOf(nodeId);
        return capacity <= 0m ? 0m : WriteLoadOf(nodeId) / capacity;
    }

    public decimal QueueingFactorOf(Guid nodeId) => CapacityModel.QueueingFactor(UtilizationOf(nodeId));

    public IReadOnlyDictionary<Guid, decimal> QueueingFactors =>
        _graph.Nodes.ToDictionary(n => n.Id, n => QueueingFactorOf(n.Id));

    public bool IsIdle => _nodeLoad.Values.All(load => load <= 0m);

    public decimal BacklogRps { get; private set; }

    // Traffic counts where it enters, and only there: a door only the telemetry consults is still
    // an entry, a number left on a box that stopped being one is not (ArchitectureGraph.DeclaresTraffic).
    public decimal EnteringRps => _graph.Nodes.Sum(n => NodeSettings.Admit(n, DeclaredRps(n)));

    public decimal LoadAmplification
    {
        get
        {
            var entering = EnteringRps;
            if (entering <= 0m)
                return 1m;

            var busiest = _graph.Nodes.Max(n => LoadOf(n.Id));
            return Math.Round(busiest / entering, 2);
        }
    }


    private decimal DeclaredRps(Node node) =>
        _graph.DeclaresTraffic(node) ? NodeSettings.DeclaredRps(node) * _demandScale : 0m;

    private void Propagate(Func<Connection, decimal> amplification)
    {
        _nodeLoad.Clear();
        _nodeDemand.Clear();
        _edgeLoad.Clear();
        _writeLoad.Clear();
        BacklogRps = 0m;

        foreach (var nodeId in _order)
        {
            var node = _graph.NodesById[nodeId];

            var incoming = _graph.IncomingByTarget[nodeId]
                .Where(edge => !_backEdges.Contains(edge.Id))
                .Sum(edge => _edgeLoad.GetValueOrDefault(edge.Id));

            var demand = DeclaredRps(node) + incoming;
            var load = NodeSettings.Admit(node, demand);
            _nodeDemand[nodeId] = demand;
            _nodeLoad[nodeId] = load;

            if (_writeCapacity.ContainsKey(nodeId))
                _writeLoad[nodeId] = _graph.IncomingByTarget[nodeId]
                    .Where(edge => !_backEdges.Contains(edge.Id) && _graph.ResilienceOf(edge).DeclaresWrite)
                    .Sum(edge => _edgeLoad.GetValueOrDefault(edge.Id));

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

        var fanOut = outgoing.Count(edge => !NodeRoles.IsTelemetryEdge(_graph, edge));

        foreach (var edge in outgoing)
        {
            if (NodeRoles.IsTelemetryEdge(_graph, edge))
            {
                _edgeLoad[edge.Id] = 0m;
                continue;
            }

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
        NodeType.Cache => load * (1m - NodeSettings.HitRatio(node)),

        NodeType.Cdn => load * (1m - NodeSettings.HitRatio(node)),

        NodeType.Client => load / fanOut,

        NodeType.Queue => Deliver(load / fanOut, edge),

        _ when NodeRoles.IsRouter(node.Type) => load / fanOut,

        _ => load
    };

    private decimal Deliver(decimal share, Connection edge)
    {
        var consumerCapacity = CapacityOf(edge.TargetNodeId);
        var delivered = Math.Min(share, consumerCapacity);

        BacklogRps += share - delivered;
        return delivered;
    }

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

        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
            if (!color.ContainsKey(node.Id))
                Visit(node.Id);

        postorder.Reverse();
        return (postorder, backEdges);
    }
}
