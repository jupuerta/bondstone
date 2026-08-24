namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// V2 D5.6. Every numeric decision of the product in one place: these are what feedback will move
/// and what the reference architectures pin down. Not configurable by the user - an analyzer whose
/// thresholds the user changes stops having an opinion, and the opinion is the product.
/// </summary>
public static class RuleThresholds
{
    public const int SyncChainWarning = 3;      // components
    public const int SyncChainCritical = 5;
    public const int CouplingWarning = 4;       // fan-out
    public const int CouplingCritical = 7;

    /// <summary>
    /// V13 D2.2. The same measurement, a different bar, for a component whose <em>job</em> is to sit
    /// in front of many others (<see cref="Rules.NodeRoles.IsRouter"/>). Four dependencies on a
    /// service is a service that has grown responsibilities; four on a gateway is a gateway with
    /// four routes.
    ///
    /// Double, and not "no limit": a gateway with twenty things behind it is the front door of a
    /// monolith, and the rule should still be able to say so. Doubling is arguable, which is the
    /// property D5.6 asks of a threshold - it can be wrong, but it can be understood.
    /// </summary>
    public const int RouterCouplingWarning = 8;
    public const int RouterCouplingCritical = 12;
    public const int RetryAmplificationMax = 10; // accumulated factor
    public const int CacheMinConsumers = 2;

    /// <summary>
    /// QA §4. Two services on one schema is already the integration-by-database anti-pattern;
    /// there is no number of services at which it becomes fine.
    /// </summary>
    public const int SharedDatabaseMinServices = 2;
    // V14 §4.3. TRAFFIC_REJECTED: below half it is a limit shedding a peak, which is the limit
    // doing its job; from half up the limit is standing in for capacity, and from nine tenths the
    // drawing says "we turn away nearly everyone" - which deserves the loud colour.
    public const decimal RejectedTrafficWarning = 0.5m;
    public const decimal RejectedTrafficCritical = 0.9m;

    // V14 §4.3. MISSING_CDN: the declared traffic at which serving directly starts being the
    // finding. Twice an Api's capacity - below that, one door handles it and a CDN is an
    // optimisation; above it, the edge is where the first real relief lives.
    public const int CdnWorthTraffic = 1000;
}
