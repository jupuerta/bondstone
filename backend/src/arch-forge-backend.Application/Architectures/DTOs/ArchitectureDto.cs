namespace bondstone_backend.Application.Architectures.DTOs;

public class ArchitectureDto
{
    public string Name { get; set; } = string.Empty;
    public string ShareToken { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<NodeDto> Nodes { get; set; } = new();
    public List<ConnectionDto> Connections { get; set; } = new();
}
