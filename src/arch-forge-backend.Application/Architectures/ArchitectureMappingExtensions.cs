using System.Text.Json;
using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;
using arch_forge_backend.Application.Analysis;

namespace arch_forge_backend.Application.Architectures;

public static class ArchitectureMappingExtensions
{
    public static ArchitectureDto ToDto(this Architecture architecture) => new()
    {
        Name = architecture.Name,
        ShareToken = architecture.ShareToken,
        Version = architecture.Version,
        CreatedAt = architecture.CreatedAt,
        UpdatedAt = architecture.UpdatedAt,
        Nodes = architecture.Nodes.Select(n => n.ToDto()).ToList(),
        Connections = architecture.Connections.Select(c => c.ToDto(TypesOf(architecture))).ToList()
    };

    public static ArchitectureCreatedDto ToCreatedDto(this Architecture architecture) => new()
    {
        Name = architecture.Name,
        ShareToken = architecture.ShareToken,
        EditToken = architecture.EditToken,
        Version = architecture.Version,
        CreatedAt = architecture.CreatedAt,
        UpdatedAt = architecture.UpdatedAt,
        Nodes = architecture.Nodes.Select(n => n.ToDto()).ToList(),
        Connections = architecture.Connections.Select(c => c.ToDto(TypesOf(architecture))).ToList()
    };

    public static NodeDto ToDto(this Node node) => new()
    {
        Id = node.Id,
        Type = node.Type,
        Name = node.Name,
        Position = new PositionDto { X = node.PositionX, Y = node.PositionY },
        Configuration = ParseConfiguration(node.Configuration)
    };

    // The protocol is derived from the two ends and travels read-only: what the client sends in this
    // field is ignored, what it reads back is the table in Protocols.
    public static ConnectionDto ToDto(this Connection connection, IReadOnlyDictionary<Guid, NodeType> types) => new()
    {
        Id = connection.Id,
        SourceNodeId = connection.SourceNodeId,
        TargetNodeId = connection.TargetNodeId,
        Protocol = Protocols.Between(TypeOf(types, connection.SourceNodeId), TypeOf(types, connection.TargetNodeId)),
        Synchronous = connection.Synchronous,
        Configuration = ParseConfiguration(connection.Configuration)
    };

    private static IReadOnlyDictionary<Guid, NodeType> TypesOf(Architecture architecture) =>
        architecture.Nodes.ToDictionary(n => n.Id, n => n.Type);

    private static NodeType? TypeOf(IReadOnlyDictionary<Guid, NodeType> types, Guid nodeId) =>
        types.TryGetValue(nodeId, out var type) ? type : null;

    private static JsonElement ParseConfiguration(string? configuration)
    {
        using var document = JsonDocument.Parse(
            string.IsNullOrWhiteSpace(configuration) ? Node.EmptyConfiguration : configuration);

        return document.RootElement.Clone();
    }
}
