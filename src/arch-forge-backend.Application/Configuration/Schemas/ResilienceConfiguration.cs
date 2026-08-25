using System;
namespace arch_forge_backend.Application.Configuration.Schemas;

public class ResilienceConfiguration
{
    public int? TimeoutSeconds { get; set; }

    public int? Retries { get; set; }
    public bool CircuitBreaker { get; set; }
    public bool Fallback { get; set; }

    public string? Access { get; set; }

    public bool Reads => !string.Equals(Access, "write", StringComparison.OrdinalIgnoreCase);

    public bool Writes => !string.Equals(Access, "read", StringComparison.OrdinalIgnoreCase);

    public bool DeclaresWrite => string.Equals(Access, "write", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(Access, "both", StringComparison.OrdinalIgnoreCase);

    public bool DeclaresRead => string.Equals(Access, "read", StringComparison.OrdinalIgnoreCase);
}
