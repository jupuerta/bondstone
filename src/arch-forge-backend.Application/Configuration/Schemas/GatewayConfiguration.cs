namespace arch_forge_backend.Application.Configuration.Schemas;

public class GatewayConfiguration
{
    public int? Timeout { get; set; }
    public int? Retries { get; set; }
    public int? RateLimit { get; set; }
}
