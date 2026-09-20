namespace bondstone_backend.Application.Configuration.Schemas;

public class DatabaseConfiguration
{
    public string? Type { get; set; }
    public int? Replicas { get; set; }
    public int? ReadReplicas { get; set; }
}
