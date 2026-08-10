using System.Text.Json;
using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Architectures;

public static class ArchitectureMappingExtensions
{
    public static ArchitectureDto ToDto(this Architecture architecture) => new()
    {
        Id = architecture.Id,
        Name = architecture.Name,
        ShareToken = architecture.ShareToken,
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

    // Configuration is stored as jsonb but surfaced to the client as a JSON object, not as an
    // escaped string - see Technical_Design_V1 §6.2. The column is not null, so this always
    // yields an object; the fallback only covers rows written before that constraint existed.
    private static JsonElement ParseConfiguration(string? configuration)
    {
        using var document = JsonDocument.Parse(
            string.IsNullOrWhiteSpace(configuration) ? Node.EmptyConfiguration : configuration);

        return document.RootElement.Clone();
    }
}
