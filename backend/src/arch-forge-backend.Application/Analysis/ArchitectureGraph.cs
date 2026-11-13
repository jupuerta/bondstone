using System.Text.Json;
using bondstone_backend.Application.Configuration.Schemas;
using bondstone_backend.Domain.Entities;
using bondstone_backend.Domain.Enums;

namespace bondstone_backend.Application.Analysis;

public class ArchitectureGraph
{
    private static readonly JsonSerializerOptions ConfigurationOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Dictionary<Guid, ResilienceConfiguration> _resilienceByConnection = new();

    public ArchitectureGraph(IReadOnlyList<Node> nodes, IReadOnlyList<Connection> connections)
    {
        NodesById = nodes.ToDictionary(n => n.Id);

        Nodes = nodes
            .OrderBy(n => n.Type)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .ThenBy(n => n.Id)
            .ToList();

        Connections = connections
            .OrderBy(c => NameOf(c.SourceNodeId), StringComparer.Ordinal)
            .ThenBy(c => NameOf(c.TargetNodeId), StringComparer.Ordinal)
            .ThenBy(c => c.Id)
            .ToList();

        OutgoingBySource = Connections.ToLookup(c => c.SourceNodeId);
        IncomingByTarget = Connections.ToLookup(c => c.TargetNodeId);

        var nodesWithDependents = Connections.Select(c => c.TargetNodeId).ToHashSet();
        RootNodes = Nodes.Where(n => !nodesWithDependents.Contains(n.Id)).ToList();

        OrphanNodes = Nodes
            .Where(n => !OutgoingBySource[n.Id].Any() && !IncomingByTarget[n.Id].Any())
            .Select(n => n.Id)
            .ToHashSet();

        // Where a request enters: a node nobody calls, that calls someone. A box with no edges at all
        // is a root by the letter and an entry point by nothing else — counted as one, deleting a call
        // turned its target into a «point of entry affected» and passed for a contained failure
        // (QA 30-08, §3.6). Telemetry is the other letter-of-the-law root: an observability box that
        // consults components calls someone, and a component only it consults is called by nobody
        // that carries a request — the same reading TrafficRoots gives the rules. Roots keep the
        // loose boxes and the telemetry: reachability and the load model need them.
        EntryNodes = Nodes
            .Where(n => !Rules.NodeRoles.IsTelemetry(n.Type)
                        && !OrphanNodes.Contains(n.Id)
                        && !IncomingByTarget[n.Id].Any(edge => !Rules.NodeRoles.IsTelemetryEdge(this, edge)))
            .ToList();

        _entryNodeIds = EntryNodes.Select(n => n.Id).ToHashSet();

        // What a person is waiting for. A flow born in a clock is work, not traffic: counting the
        // cron as a point of entry made «the users notice nothing» read as 1 of 2 affected and 100 %
        // of requests lost while the users were served whole (QA 30-08, §10.3 point 10). The manual
        // has said it all along: a flow that starts at a Scheduler is not user traffic.
        UserEntryNodes = EntryNodes
            .Where(n => Rules.NodeRoles.ReceivesFromOutside(n.Type) || Rules.NodeRoles.IsTrafficOrigin(n.Type))
            .ToList();
    }

    private readonly HashSet<Guid> _entryNodeIds;

    // Where a declared number counts, and the only place: an entry node, or a client — the origin by
    // definition, even when something calls it back. A number declared while a box was an entry kept
    // adding up once a door was put in front of it (the inspector hides the field, it did not delete
    // it), and what sat behind was sized for traffic that never arrives (QA 30-08, §10.3 point 8). A
    // loose box declares nothing either (§9.5). The load model and `declaresLoad` read this together.
    public bool DeclaresTraffic(Node node) =>
        !OrphanNodes.Contains(node.Id)
        && (Rules.NodeRoles.IsTrafficOrigin(node.Type) || _entryNodeIds.Contains(node.Id));

    public IReadOnlyList<Node> Nodes { get; }

    public IReadOnlyList<Connection> Connections { get; }

    public IReadOnlyDictionary<Guid, Node> NodesById { get; }

    public ILookup<Guid, Connection> OutgoingBySource { get; }

    public ILookup<Guid, Connection> IncomingByTarget { get; }

    public IReadOnlyList<Node> RootNodes { get; }

    public IReadOnlyList<Node> EntryNodes { get; }

    public IReadOnlyList<Node> UserEntryNodes { get; }

    // What the model believes of a call, as opposed to what the drawing declares. A gateway or a
    // load balancer has nothing to answer with, so a fallback ticked on its call does not exist for
    // the simulation: it neither serves requests nor contains a failure (QA 30-08, §1.3, 7.4). The
    // rules keep reading the declaration — IMPLAUSIBLE_DEPENDENCY is the one that says it is wrong.
    public ResilienceConfiguration EffectiveResilienceOf(Connection connection)
    {
        var declared = ResilienceOf(connection);

        if (!declared.Fallback || !Rules.NodeRoles.ForwardsRequest(this, connection))
            return declared;

        return new ResilienceConfiguration
        {
            TimeoutSeconds = declared.TimeoutSeconds,
            Retries = declared.Retries,
            CircuitBreaker = declared.CircuitBreaker,
            Fallback = false,
            Access = declared.Access
        };
    }

    // The one question four places ask: is there a fallback the model believes? A router has nothing
    // to answer with (§7.4) and a declared write nothing to approximate (§3.5), so on those calls the
    // ticked box exists only in the drawing — IMPLAUSIBLE_DEPENDENCY says so, and until §10.4 two
    // rules paid for it: ticking it turned a critical MISSING_RESILIENCE into a +5 warning and the
    // score went up by a box the model says is not there (QA 30-08, §10.2 point 2).
    public bool BelievesFallback(Connection connection) =>
        EffectiveResilienceOf(connection).Fallback && !ResilienceOf(connection).DeclaresWrite;

    public IReadOnlySet<Guid> OrphanNodes { get; }

    public List<ArchitectureFact> Facts { get; } = [];

    public ResilienceConfiguration ResilienceOf(Connection connection)
    {
        if (_resilienceByConnection.TryGetValue(connection.Id, out var cached))
            return cached;

        var resilience = Deserialize(connection.Configuration);
        _resilienceByConnection[connection.Id] = resilience;
        return resilience;
    }

    public ProtocolType ProtocolOf(Connection connection) =>
        Protocols.Between(
            NodesById.GetValueOrDefault(connection.SourceNodeId)?.Type,
            NodesById.GetValueOrDefault(connection.TargetNodeId)?.Type);

    private string NameOf(Guid nodeId) =>
        NodesById.GetValueOrDefault(nodeId)?.Name ?? string.Empty;

    private static ResilienceConfiguration Deserialize(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
            return new ResilienceConfiguration();

        try
        {
            return JsonSerializer.Deserialize<ResilienceConfiguration>(configuration, ConfigurationOptions)
                   ?? new ResilienceConfiguration();
        }
        catch (JsonException)
        {
            return new ResilienceConfiguration();
        }
    }
}
