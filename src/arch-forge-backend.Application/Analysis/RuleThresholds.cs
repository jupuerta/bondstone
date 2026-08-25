namespace arch_forge_backend.Application.Analysis;

public static class RuleThresholds
{
    public const int SyncChainWarning = 3;
    public const int SyncChainCritical = 5;
    public const int CouplingWarning = 4;
    public const int CouplingCritical = 7;

    public const int RouterCouplingWarning = 8;
    public const int RouterCouplingCritical = 12;
    public const int RetryAmplificationMax = 10;
    public const int CacheMinConsumers = 2;

    public const int SharedDatabaseMinServices = 2;
    public const decimal RejectedTrafficWarning = 0.5m;
    public const decimal RejectedTrafficCritical = 0.9m;

    public const int CdnWorthTraffic = 1000;

    public const int TimeoutUnboundedMs = Simulation.LatencyModel.UnboundedWait;
}
