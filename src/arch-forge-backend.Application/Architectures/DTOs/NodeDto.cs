using System.Text.Json;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Architectures.DTOs;

public class NodeDto
{
    public Guid Id { get; set; }
    public NodeType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public PositionDto Position { get; set; } = new();
    public JsonElement? Configuration { get; set; }
}
