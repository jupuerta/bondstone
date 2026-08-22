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

    // V4 D5.2. The debt V2 wrote down and V3 pushed twice, plus the one the load axis earns.
    // Security and Observability stop being two areas that travel in the contract with a dash.
    public const string OrphanNode = "ORPHAN_NODE";
    public const string QueueNoDlq = "QUEUE_NO_DLQ";
    public const string TimeoutTooShort = "TIMEOUT_TOO_SHORT";
    public const string UndersizedComponent = "UNDERSIZED_COMPONENT";
    public const string NoRateLimit = "NO_RATE_LIMIT";
    public const string ExposedDatabase = "EXPOSED_DATABASE";
    public const string UnauthenticatedEntry = "UNAUTHENTICATED_ENTRY";
    public const string NoObservability = "NO_OBSERVABILITY";

    // V8 §6.2. The rule that makes ExternalService a component and not a label.
    public const string ExternalNoFallback = "EXTERNAL_NO_FALLBACK";

    // V13 D3.1. The rule that makes ProtocolType a field somebody reads, and the only one whose
    // findings are about the drawing rather than about the architecture.
    public const string ProtocolMismatch = "PROTOCOL_MISMATCH";
}
