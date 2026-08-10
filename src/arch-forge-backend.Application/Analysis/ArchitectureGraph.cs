using System.Text.Json;
using arch_forge_backend.Application.Configuration.Schemas;
using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// The graph the rules see, with the projections six of the seven rules would otherwise rebuild
/// one by one (V2 plan técnico §6.1).
/// </summary>
public class ArchitectureGraph
{
    private static readonly JsonSerializerOptions ConfigurationOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Dictionary<Guid, ResilienceConfiguration> _resilienceByConnection = new();

    public ArchitectureGraph(IReadOnlyList<Node> nodes, IReadOnlyList<Connection> connections)
    {
        Nodes = nodes;
        Connections = connections;

        NodesById = nodes.ToDictionary(n => n.Id);
        OutgoingBySource = connections.ToLookup(c => c.SourceNodeId);
        IncomingByTarget = connections.ToLookup(c => c.TargetNodeId);

        // The invariant is "source depends on target" (D3.3), so a node nobody points at is where
        // traffic enters. SPOF's pattern B needs this, and having it here stops each rule from
        // redefining it its own way.
        var nodesWithDependents = connections.Select(c => c.TargetNodeId).ToHashSet();
        RootNodes = nodes.Where(n => !nodesWithDependents.Contains(n.Id)).ToList();
    }

    public IReadOnlyList<Node> Nodes { get; }
    public IReadOnlyList<Connection> Connections { get; }

    public IReadOnlyDictionary<Guid, Node> NodesById { get; }

    /// <summary>Edges leaving a node: what it depends on.</summary>
    public ILookup<Guid, Connection> OutgoingBySource { get; }

    /// <summary>Edges arriving at a node: its dependents.</summary>
    public ILookup<Guid, Connection> IncomingByTarget { get; }

    /// <summary>Nodes with no dependents - where traffic enters the system.</summary>
    public IReadOnlyList<Node> RootNodes { get; }

    /// <summary>
    /// What a producer rule published for consumer rules to read (V2 D5.1).
    /// </summary>
    public List<ArchitectureFact> Facts { get; } = [];

    /// <summary>
    /// The edge's resilience settings, parsed once. An edge has a single schema regardless of
    /// protocol (D3.1), so there is no registry to go through.
    /// </summary>
    public ResilienceConfiguration ResilienceOf(Connection connection)
    {
        if (_resilienceByConnection.TryGetValue(connection.Id, out var cached))
            return cached;

        var resilience = Deserialize(connection.Configuration);
        _resilienceByConnection[connection.Id] = resilience;
        return resilience;
    }

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
            // Saving validates the schema (D3.1), so this only covers rows written before that
            // validation existed. An unreadable configuration means "nothing configured", which
            // makes the rules report a missing timeout rather than fail the whole analysis.
            return new ResilienceConfiguration();
        }
    }
}
