using System.Text.Json;
using bondstone_backend.Application.Analysis.Rules;
using bondstone_backend.Application.Configuration.Schemas;
using bondstone_backend.Domain.Enums;

namespace bondstone_backend.Application.Configuration;

public static class ConfigurationRanges
{
    public const int MaxTimeoutSeconds = 600;

    public static readonly string[] Zones = ["a", "b", "c"];

    private static readonly string[] Accesses = ["read", "write", "both"];

    public static string? Violation(object configuration) => configuration switch
    {
        ApiConfiguration c => AtLeastOne("instances", c.Instances)
                              ?? AtLeastZero("requestsPerSecond", c.RequestsPerSecond)
                              ?? AtLeastOne("rateLimit", c.RateLimit),
        ServiceConfiguration c => AtLeastOne("instances", c.Instances),
        WorkerConfiguration c => AtLeastOne("instances", c.Instances),
        DatabaseConfiguration c => AtLeastZero("replicas", c.Replicas)
                                   ?? AtLeastZero("readReplicas", c.ReadReplicas),
        CacheConfiguration c => Ratio("hitRatio", c.HitRatio)
                                ?? AtLeastOne("instances", c.Instances),
        CdnConfiguration c => Ratio("hitRatio", c.HitRatio),
        GatewayConfiguration c => AtLeastZero("requestsPerSecond", c.RequestsPerSecond)
                                  ?? AtLeastOne("rateLimit", c.RateLimit)
                                  ?? AtLeastOne("instances", c.Instances),
        LoadBalancerConfiguration c => AtLeastZero("requestsPerSecond", c.RequestsPerSecond)
                                       ?? AtLeastOne("instances", c.Instances),
        ClientConfiguration c => AtLeastZero("requestsPerSecond", c.RequestsPerSecond),
        SchedulerConfiguration c => AtLeastZero("jobsPerSecond", c.JobsPerSecond),
        QueueConfiguration c => AtLeastZero("maxRetries", c.MaxRetries)
                                ?? AtLeastOne("instances", c.Instances),
        TopicConfiguration c => AtLeastOne("instances", c.Instances),
        ResilienceConfiguration c => Between("timeoutSeconds", c.TimeoutSeconds, 1, MaxTimeoutSeconds)
                                     ?? AtLeastZero("retries", c.Retries)
                                     ?? OneOf("access", c.Access, Accesses),
        _ => null
    };

    // zone is not a property of the 16 typed schemas, so Violation() never sees it: it has to be
    // read off the raw configuration object, or a typo would save silently and mean "no zone".
    public static string? ZoneViolation(NodeType type, JsonElement configuration)
    {
        foreach (var property in configuration.EnumerateObject())
        {
            if (!property.Name.Equals("zone", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!NodeRoles.IsYoursToPlace(type))
                return $"zone does not apply to {type}.";

            if (property.Value.ValueKind != JsonValueKind.String
                || !Zones.Contains(property.Value.GetString()))
                return "zone must be one of a, b, c.";
        }

        return null;
    }

    // Anything else read as «not a declared write», so two challenges shipped «ReadWrite» — a value
    // nobody parses — and taught the wrong lesson in silence (QA 30-08, §10.3 point 9).
    private static string? OneOf(string field, string? value, string[] allowed) =>
        value is not null && !allowed.Contains(value, StringComparer.OrdinalIgnoreCase)
            ? $"{field} must be one of {string.Join(", ", allowed)}."
            : null;

    private static string? AtLeastOne(string field, int? value) =>
        value is < 1 ? $"{field} must be 1 or greater." : null;

    private static string? AtLeastZero(string field, int? value) =>
        value is < 0 ? $"{field} must be 0 or greater." : null;

    private static string? Between(string field, int? value, int min, int max) =>
        value is { } v && (v < min || v > max) ? $"{field} must be between {min} and {max}." : null;

    private static string? Ratio(string field, decimal? value) =>
        value is { } v && (v < 0 || v > 1) ? $"{field} must be between 0 and 1." : null;
}
