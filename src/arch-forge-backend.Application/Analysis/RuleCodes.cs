namespace arch_forge_backend.Application.Analysis;

public static class RuleCodes
{
    public const string Spof = "SPOF";
    public const string SyncChain = "SYNC_CHAIN";
    public const string MissingResilience = "MISSING_RESILIENCE";
    public const string RetryAmplification = "RETRY_AMPLIFICATION";
    public const string HighCoupling = "HIGH_COUPLING";
    public const string Cycle = "CYCLE";
    public const string MissingCache = "MISSING_CACHE";

    public const string OrphanNode = "ORPHAN_NODE";
    public const string QueueNoDlq = "QUEUE_NO_DLQ";
    public const string TimeoutTooShort = "TIMEOUT_TOO_SHORT";
    public const string UndersizedComponent = "UNDERSIZED_COMPONENT";
    public const string NoRateLimit = "NO_RATE_LIMIT";
    public const string ExposedDatabase = "EXPOSED_DATABASE";
    public const string UnauthenticatedEntry = "UNAUTHENTICATED_ENTRY";
    public const string NoObservability = "NO_OBSERVABILITY";

    public const string ExternalNoFallback = "EXTERNAL_NO_FALLBACK";

    public const string ProtocolMismatch = "PROTOCOL_MISMATCH";

    public const string QueueNoConsumer = "QUEUE_NO_CONSUMER";
    public const string CacheAsStore = "CACHE_AS_STORE";
    public const string SharedDatabase = "SHARED_DATABASE";

    public const string TrafficRejected = "TRAFFIC_REJECTED";
    public const string MissingCdn = "MISSING_CDN";

    public const string ImplausibleDependency = "IMPLAUSIBLE_DEPENDENCY";

    public const string TimeoutTooLong = "TIMEOUT_TOO_LONG";
}
