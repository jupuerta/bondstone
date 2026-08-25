namespace arch_forge_backend.Domain.Entities;

public class Architecture
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public string ShareToken { get; set; } = string.Empty;

    public string EditToken { get; set; } = string.Empty;

    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<Node> Nodes { get; set; } = new();
    public List<Connection> Connections { get; set; } = new();
}
