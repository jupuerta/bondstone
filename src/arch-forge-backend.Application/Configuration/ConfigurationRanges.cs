using arch_forge_backend.Application.Configuration.Schemas;

namespace arch_forge_backend.Application.Configuration;

/// <summary>
/// V14 B14. <strong>The range every number in a configuration is held to on save</strong> - in one
/// place, read as a table, because the alternative was what V14 found: the edge had guards since V2
/// (<c>timeoutSeconds > 0</c>, <c>retries >= 0</c>) and the nodes had none, so <c>instances: -3</c>
/// and <c>requestsPerSecond: -5000</c> were stored without a word and scored a hundred.
///
/// <strong>The analyzer already clamps every one of these</strong> (<see cref="Simulation.NodeSettings"/>
/// reads a negative as "none", <see cref="Simulation.NodeSettings.HitRatio"/> pins to [0, 1]) and
/// keeps doing so, for the rows written before this existed. That is not the problem this solves.
/// The problem is that the user typed a number, the system accepted it, and it meant something
/// else - and in the case of a negative RPS what it meant was "switch the whole load axis off":
/// UNDERSIZED_COMPONENT and NO_RATE_LIMIT stopped applying and the configuration that cannot exist
/// came out cleaner than the one that can.
///
/// The floors say what the field means. <c>instances</c> and <c>rateLimit</c> start at 1 because
/// zero of either is not a smaller amount, it is the absence the field exists to replace (V14 B4
/// already reads <c>instances: 0</c> as "none"); the loads, the retries and the replicas start at 0
/// because zero of those is a real declaration ("no replicas", V4 D4.3); a ratio is a ratio.
/// </summary>
public static class ConfigurationRanges
{
    /// <summary>
    /// The one ceiling. A timeout is "the key to all the others" (<see cref="ResilienceConfiguration.TimeoutSeconds"/>)
    /// because it is what turns a hung call into an error somebody can catch - and a timeout of
    /// twenty-seven hours (V14 T6: <c>100000</c>, accepted, counted as protection) catches
    /// nothing. Ten minutes is over any request a caller actually waits for, and under the hours
    /// the QA called absurd; whether a timeout that is merely <em>long</em> still protects is a
    /// rule's question, not a save's, and that rule is still owed (QA B14).
    /// </summary>
    public const int MaxTimeoutSeconds = 600;

    /// <summary>The first field out of range, as the sentence the 400 carries; null when every field is within its range.</summary>
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
