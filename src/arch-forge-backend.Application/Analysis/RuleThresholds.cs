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
    public const int RetryAmplificationMax = 10; // accumulated factor
    public const int CacheMinConsumers = 2;
}
