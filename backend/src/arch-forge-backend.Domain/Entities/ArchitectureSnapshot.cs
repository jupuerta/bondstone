namespace bondstone_backend.Domain.Entities;

public class ArchitectureSnapshot
{
    public Guid Id { get; set; }
    public Guid ArchitectureId { get; set; }

    public int Version { get; set; }

    public string ScoringVersion { get; set; } = string.Empty;

    public int? Score { get; set; }

    public string Summary { get; set; } = "{}";

    public string Areas { get; set; } = "{}";

    public DateTime CreatedAt { get; set; }
}
