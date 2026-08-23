using arch_forge_backend.Application.Configuration;
using arch_forge_backend.Application.Configuration.Schemas;
using Xunit;

namespace arch_forge_backend.Application.Tests.Configuration;

/// <summary>
/// V14 B14. The table, row by row: every floor, the one ceiling, and the meaning of each - zero
/// instances is none, zero replicas is a declaration, a ratio is a ratio.
/// </summary>
public class ConfigurationRangesTests
{
    public static TheoryData<string, object> OutOfRange => new()
    {
        { "instances must be 1 or greater.", new ApiConfiguration { Instances = -3 } },
        { "instances must be 1 or greater.", new ApiConfiguration { Instances = 0 } },
        { "instances must be 1 or greater.", new ServiceConfiguration { Instances = 0 } },
        { "instances must be 1 or greater.", new WorkerConfiguration { Instances = -1 } },
        { "requestsPerSecond must be 0 or greater.", new ApiConfiguration { RequestsPerSecond = -5000 } },
        { "requestsPerSecond must be 0 or greater.", new ClientConfiguration { RequestsPerSecond = -5000 } },
        { "requestsPerSecond must be 0 or greater.", new GatewayConfiguration { RequestsPerSecond = -1 } },
        { "requestsPerSecond must be 0 or greater.", new LoadBalancerConfiguration { RequestsPerSecond = -1 } },
        { "rateLimit must be 1 or greater.", new ApiConfiguration { RateLimit = 0 } },
        { "rateLimit must be 1 or greater.", new GatewayConfiguration { RateLimit = -100 } },
        { "replicas must be 0 or greater.", new DatabaseConfiguration { Replicas = -3 } },
        { "readReplicas must be 0 or greater.", new DatabaseConfiguration { ReadReplicas = -1 } },
        { "hitRatio must be between 0 and 1.", new CacheConfiguration { HitRatio = 5 } },
        { "hitRatio must be between 0 and 1.", new CacheConfiguration { HitRatio = -0.1m } },
        { "hitRatio must be between 0 and 1.", new CdnConfiguration { HitRatio = 1.5m } },
        { "jobsPerSecond must be 0 or greater.", new SchedulerConfiguration { JobsPerSecond = -1 } },
        { "maxRetries must be 0 or greater.", new QueueConfiguration { MaxRetries = -1 } },
        { "timeoutSeconds must be between 1 and 600.", new ResilienceConfiguration { TimeoutSeconds = 0 } },
        { "timeoutSeconds must be between 1 and 600.", new ResilienceConfiguration { TimeoutSeconds = -5 } },
        { "timeoutSeconds must be between 1 and 600.", new ResilienceConfiguration { TimeoutSeconds = 100000 } },
        { "retries must be 0 or greater.", new ResilienceConfiguration { Retries = -1 } }
    };

    [Theory]
    [MemberData(nameof(OutOfRange))]
    public void A_number_outside_its_range_is_named(string expected, object configuration)
    {
        Assert.Equal(expected, ConfigurationRanges.Violation(configuration));
    }

    public static TheoryData<object> AtTheEdge => new()
    {
        new ApiConfiguration { Instances = 1, RequestsPerSecond = 0, RateLimit = 1 },
        new DatabaseConfiguration { Replicas = 0, ReadReplicas = 0 },
        new CacheConfiguration { HitRatio = 0 },
        new CacheConfiguration { HitRatio = 1 },
        new CdnConfiguration { HitRatio = 0.9m },
        new SchedulerConfiguration { JobsPerSecond = 0 },
        new QueueConfiguration { MaxRetries = 0 },
        new ResilienceConfiguration { TimeoutSeconds = 1, Retries = 0 },
        new ResilienceConfiguration { TimeoutSeconds = ConfigurationRanges.MaxTimeoutSeconds }
    };

    [Theory]
    [MemberData(nameof(AtTheEdge))]
    public void The_edge_of_a_range_is_inside_it(object configuration)
    {
        Assert.Null(ConfigurationRanges.Violation(configuration));
    }

    [Fact]
    public void Undeclared_is_not_out_of_range()
    {
        // Absent is "nobody said" everywhere in the schema (V4 DT.6, V13 D2.1), and a guard that
        // rejected silence would reject every diagram drawn before the field existed.
        Assert.Null(ConfigurationRanges.Violation(new ApiConfiguration()));
        Assert.Null(ConfigurationRanges.Violation(new ResilienceConfiguration()));
        Assert.Null(ConfigurationRanges.Violation(new ExternalServiceConfiguration()));
    }

    [Fact]
    public void The_first_violation_is_the_one_reported()
    {
        // One sentence per 400, in field order - the same shape the edge's guards had since V2.
        Assert.Equal(
            "instances must be 1 or greater.",
            ConfigurationRanges.Violation(new ApiConfiguration { Instances = 0, RequestsPerSecond = -1 }));
    }
}
