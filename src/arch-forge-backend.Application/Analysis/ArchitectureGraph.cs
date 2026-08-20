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
        NodesById = nodes.ToDictionary(n => n.Id);

        // V6 D1.1. The two lists every rule iterates are ordered here, once. EF Core guarantees no
        // order without an OrderBy and the repository does not apply one, so before this the same
        // graph could arrive in two different orders and the rules would emit their findings in
        // two different orders - which the analyzer's tie-break then preserved (V5 §12).
        //
        // The order is the one the export already composes its tables in (V5 §6.2): grouped by
        // type and alphabetical inside it, which is how an inventory is read. Id breaks the tie
        // because two components can share a name.
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

        // Built from the ordered lists, not from the arguments: a lookup enumerates each group in
        // insertion order, so a rule walking IncomingByTarget would otherwise inherit exactly the
        // order this constructor has just removed.
        OutgoingBySource = Connections.ToLookup(c => c.SourceNodeId);
        IncomingByTarget = Connections.ToLookup(c => c.TargetNodeId);

        // The invariant is "source depends on target" (D3.3), so a node nobody points at is where
        // traffic enters. SPOF's pattern B needs this, and having it here stops each rule from
        // redefining it its own way.
        var nodesWithDependents = Connections.Select(c => c.TargetNodeId).ToHashSet();
        RootNodes = Nodes.Where(n => !nodesWithDependents.Contains(n.Id)).ToList();
    }

    /// <summary>
    /// The components, ordered by <c>(Type, Name, Id)</c>. <strong>The order is contract</strong>
    /// (V6 D1.1): it is what makes the analysis of a graph independent of the order it was read
    /// from the database in.
    /// </summary>
    public IReadOnlyList<Node> Nodes { get; }

    /// <summary>
    /// The edges, ordered by source name, then target name, then <c>Id</c> - so they read
    /// following the flow. Contract, for the same reason as <see cref="Nodes"/>.
    /// </summary>
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

    /// <summary>
    /// The name an edge hangs from, for ordering. An edge pointing at a node that is not in the
    /// list sorts under the empty name instead of throwing: ordering is not the place to discover
    /// a broken graph.
    /// </summary>
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
            // Saving validates the schema (D3.1), so this only covers rows written before that
            // validation existed. An unreadable configuration means "nothing configured", which
            // makes the rules report a missing timeout rather than fail the whole analysis.
            return new ResilienceConfiguration();
        }
    }
}
