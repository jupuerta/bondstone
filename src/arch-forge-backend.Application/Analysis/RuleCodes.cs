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

    // QA §4 (docs/QA-analisis.md). Three lessons the model could already teach and nobody was
    // asking for: work handed to a queue nobody drains, state kept in a cache with no origin
    // behind it, and services meeting on one schema.
    public const string QueueNoConsumer = "QUEUE_NO_CONSUMER";
    public const string CacheAsStore = "CACHE_AS_STORE";
    public const string SharedDatabase = "SHARED_DATABASE";

    // V14 §4.3 (docs/V14/QA-analisis-V14.md). The two rules that balance the areas B13 found
    // lopsided - Availability had one rule and Scalability two while Resilience had eight - and
    // each one is a lesson the model could already measure. TRAFFIC_REJECTED: a rate limit
    // shedding most of the declared demand had been making everything behind it look healthy.
    // MISSING_CDN: the rule that finally justifies the Cdn type the way D3.2 demands.
    public const string TrafficRejected = "TRAFFIC_REJECTED";
    public const string MissingCdn = "MISSING_CDN";

    // V14 §4.1 (docs/V14/QA-analisis-V14.md). PROTOCOL_MISMATCH's idea applied to the ends of the
    // arrow instead of to its protocol: whether the two types it joins describe a call that can
    // happen at all. The second rule whose findings are about the drawing, and the one that
    // stops `Db -> Client` scoring 98.
    public const string ImplausibleDependency = "IMPLAUSIBLE_DEPENDENCY";

    // V15 E2 (docs/V15/QA-analisis-V15.md). TIMEOUT_TOO_SHORT's twin, and the rule
    // ConfigurationRanges.MaxTimeoutSeconds said was still owed: a timeout at or above what the
    // simulator already treats as an unbounded wait protects exactly as much as no timeout at all.
    // Twenty-four rules.
    public const string TimeoutTooLong = "TIMEOUT_TOO_LONG";
}
