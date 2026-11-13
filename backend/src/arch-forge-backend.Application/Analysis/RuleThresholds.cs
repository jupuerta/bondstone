namespace bondstone_backend.Application.Analysis;

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
    // QA 30-08 §3.4. A limit at half the declared traffic halves the load of everything behind it,
    // switches UNDERSIZED_COMPONENT and NO_RATE_LIMIT off, and at 0.5 this rule said nothing: six
    // load challenges were passed by shedding 48 % of the customers. Shedding one in five is already
    // a design statement; shedding half of them is the drawing hiding its capacity problem.
    public const decimal RejectedTrafficWarning = 0.2m;
    public const decimal RejectedTrafficCritical = 0.5m;

    public const int CdnWorthTraffic = 1000;

    public const decimal OverprovisionedUtilization = 0.2m;

    // V22 D1. Above this multiple of what the load needs, waste stops being a warning.
    //
    // `Cost` is one of the two areas fed by a single rule, and a code's penalty is capped at three
    // times its weight — so with `OVERPROVISIONED` stuck at `Warning`, the area could never fall
    // below 85 however much money was being burned. Measured before choosing the number: an `Api`
    // with **200 copies for 10 rps** scored the same 98 as the same drawing sized correctly. Paying
    // twenty times over cost exactly nothing.
    //
    // Four and not two: three copies for a load that needs one is a defensible amount of headroom,
    // and the rule already refuses to advise below `RedundancyFloor`. Four is where the drawing
    // stops being cautious and starts being wrong.
    public const int OverprovisionedCritical = 4;

    // Never advise dropping below this: the second copy is not capacity, it is what takes the
    // component out of SPOF. Same number NodeSettings calls RedundantInstances.
    public const int RedundancyFloor = 2;

    public const int TimeoutUnboundedMs = Simulation.LatencyModel.UnboundedWait;
}
