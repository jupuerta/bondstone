using arch_forge_backend.Application.Configuration.Schemas;

namespace arch_forge_backend.Application.Configuration;

public static class ConfigurationRanges
{
    public const int MaxTimeoutSeconds = 600;

    public static string? Violation(object configuration) => configuration switch
    {
        ApiConfiguration c => AtLeastOne("instances", c.Instances)
                              ?? AtLeastZero("requestsPerSecond", c.RequestsPerSecond)
                              ?? AtLeastOne("rateLimit", c.RateLimit),
        ServiceConfiguration c => AtLeastOne("instances", c.Instances),
        WorkerConfiguration c => AtLeastOne("instances", c.Instances),
        DatabaseConfiguration c => AtLeastZero("replicas", c.Replicas)
                                   ?? AtLeastZero("readReplicas", c.ReadReplicas),
        CacheConfiguration c => Ratio("hitRatio", c.HitRatio),
        CdnConfiguration c => Ratio("hitRatio", c.HitRatio),
        GatewayConfiguration c => AtLeastZero("requestsPerSecond", c.RequestsPerSecond)
                                  ?? AtLeastOne("rateLimit", c.RateLimit),
        LoadBalancerConfiguration c => AtLeastZero("requestsPerSecond", c.RequestsPerSecond),
        ClientConfiguration c => AtLeastZero("requestsPerSecond", c.RequestsPerSecond),
        SchedulerConfiguration c => AtLeastZero("jobsPerSecond", c.JobsPerSecond),
        QueueConfiguration c => AtLeastZero("maxRetries", c.MaxRetries),
        ResilienceConfiguration c => Between("timeoutSeconds", c.TimeoutSeconds, 1, MaxTimeoutSeconds)
                                     ?? AtLeastZero("retries", c.Retries),
        _ => null
    };

    private static string? AtLeastOne(string field, int? value) =>
        value is < 1 ? $"{field} must be 1 or greater." : null;

    private static string? AtLeastZero(string field, int? value) =>
        value is < 0 ? $"{field} must be 0 or greater." : null;

    private static string? Between(string field, int? value, int min, int max) =>
        value is { } v && (v < min || v > max) ? $"{field} must be between {min} and {max}." : null;

    private static string? Ratio(string field, decimal? value) =>
        value is { } v && (v < 0 || v > 1) ? $"{field} must be between 0 and 1." : null;
}
