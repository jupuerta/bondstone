namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// V4 DT.3. A cache lets through only what it misses, and that is the one number of a cache that
/// changes anything: <c>carga saliente = carga entrante × (1 − hitRatio)</c>. It is what makes
/// Challenge #004 solvable - put a cache in front and the database's utilisation falls to a fifth.
///
/// <c>Strategy</c> and <c>TtlSeconds</c> do <strong>not</strong> come back. A TTL is recovery over
/// time and there is still no time in the model (V3 D1.4); a strategy would need reads told apart
/// from writes, which the model still does not do. One honest number beats two decorative ones.
/// </summary>
public class CacheConfiguration
{
    /// <summary>Between 0 and 1. Absent means <see cref="Simulation.CapacityModel.DefaultHitRatio"/>.</summary>
    public decimal? HitRatio { get; set; }
}
