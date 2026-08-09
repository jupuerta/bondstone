namespace arch_forge_backend.Application.Configuration.Schemas;

public class ServiceConfiguration
{
    public int? Instances { get; set; }
    public int? Timeout { get; set; }
    public int? Retries { get; set; }
}
