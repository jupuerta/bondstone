namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// Codes travel as strings on the wire (V2 D2.4): an unknown code paints with a default style
/// instead of breaking deserialization for a tab that has not been reloaded since the deploy. The
/// constants cover the real risk, which is silent typos that stop findings from grouping.
/// A code is never reused nor given a new meaning.
/// </summary>
public static class RuleCodes
{
    public const string Spof = "SPOF";
    public const string SyncChain = "SYNC_CHAIN";
    public const string MissingResilience = "MISSING_RESILIENCE";
    public const string RetryAmplification = "RETRY_AMPLIFICATION";
    public const string HighCoupling = "HIGH_COUPLING";
    public const string Cycle = "CYCLE";
    public const string MissingCache = "MISSING_CACHE";
}
