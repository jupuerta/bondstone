namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// V3 D3.5. Without time in the model (D1.4) a TTL means nothing, and a caching strategy would
/// need reads told apart from writes, which the model does not do.
/// </summary>
public class CacheConfiguration
{
}
