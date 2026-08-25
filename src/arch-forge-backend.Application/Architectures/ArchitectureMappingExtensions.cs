using System.Text.Json;
using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Domain.Entities;

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
        Connections = architecture.Connections.Select(c => c.ToDto()).ToList()
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
        Connections = architecture.Connections.Select(c => c.ToDto()).ToList()
    };

    public static NodeDto ToDto(this Node node) => new()
    {
        Id = node.Id,
        Type = node.Type,
        Name = node.Name,
        Position = new PositionDto { X = node.PositionX, Y = node.PositionY },
        Configuration = ParseConfiguration(node.Configuration)
    };

    public static ConnectionDto ToDto(this Connection connection) => new()
    {
        Id = connection.Id,
        SourceNodeId = connection.SourceNodeId,
        TargetNodeId = connection.TargetNodeId,
        Protocol = connection.Protocol,
        Synchronous = connection.Synchronous,
        Configuration = ParseConfiguration(connection.Configuration)
    };

    private static JsonElement ParseConfiguration(string? configuration)
    {
        using var document = JsonDocument.Parse(
            string.IsNullOrWhiteSpace(configuration) ? Node.EmptyConfiguration : configuration);

        return document.RootElement.Clone();
    }
}
