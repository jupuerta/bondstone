namespace arch_forge_backend.Application.Configuration.Schemas;

public class ApiConfiguration
{
    public string? Protocol { get; set; }
    public int? Timeout { get; set; }
    public int? Retries { get; set; }
}
