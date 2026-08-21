namespace arch_forge_backend.Application.Architectures.DTOs;

/// <summary>
/// What every read of an architecture carries - by share token or by edit token alike. Never the
/// database id: it stopped being a value a client needs the moment writes moved to
/// <see cref="ArchitectureCreatedDto.EditToken"/>, and a field that looks like a credential tends to
/// get used like one.
/// </summary>
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
