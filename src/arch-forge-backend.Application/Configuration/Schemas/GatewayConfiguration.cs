namespace arch_forge_backend.Application.Configuration.Schemas;

public class GatewayConfiguration
{
    public int? RequestsPerSecond { get; set; }

    public int? RateLimit { get; set; }

    public bool? Authentication { get; set; }
}
