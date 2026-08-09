namespace arch_forge_backend.Application.Configuration.Schemas;

public class CacheConfiguration
{
    public string? Strategy { get; set; }
    public int? TtlSeconds { get; set; }
}
