using System.Text.Json;
using arch_forge_backend.Application.Configuration.Schemas;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Analysis;

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
    }

    public IReadOnlyList<Node> Nodes { get; }

    public IReadOnlyList<Connection> Connections { get; }

    public IReadOnlyDictionary<Guid, Node> NodesById { get; }

    public ILookup<Guid, Connection> OutgoingBySource { get; }

    public ILookup<Guid, Connection> IncomingByTarget { get; }

    public IReadOnlyList<Node> RootNodes { get; }

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
