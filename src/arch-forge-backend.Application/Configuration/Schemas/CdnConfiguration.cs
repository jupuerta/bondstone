namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// QA §3.2. Same single lever as <see cref="CacheConfiguration"/>, same reason: the one number of
/// an edge cache that changes anything is how much it absorbs.
/// </summary>
public class CdnConfiguration
{
    /// <summary>
    /// Share of incoming traffic served at the edge, 0-1. Empty means
    /// <c>CapacityModel.DefaultCdnHitRatio</c> (0.9): static content in front of an origin is the
    /// best-hit-ratio cache there is, and the default should teach that.
    /// </summary>
    public decimal? HitRatio { get; set; }
}
