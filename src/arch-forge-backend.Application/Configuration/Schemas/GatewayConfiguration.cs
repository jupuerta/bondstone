namespace arch_forge_backend.Application.Configuration.Schemas;

public class GatewayConfiguration
{
    public int? RequestsPerSecond { get; set; }

    public int? RateLimit { get; set; }

    public bool? Authentication { get; set; }

    // QA 30-08 §11.4. A gateway is a stateless box that forwards requests, and in production it runs
    // as several behind one address: the rule that measured it could already say «raise instances»
    // and the inspector had no such field to raise.
    public int? Instances { get; set; }
}
