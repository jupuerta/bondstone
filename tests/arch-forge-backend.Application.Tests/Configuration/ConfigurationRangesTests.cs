using System.Text.Json;
using bondstone_backend.Application.Configuration;
using bondstone_backend.Application.Configuration.Schemas;
using bondstone_backend.Domain.Enums;
using Xunit;

namespace bondstone_backend.Application.Tests.Configuration;

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
        Assert.Null(ConfigurationRanges.Violation(new ApiConfiguration()));
        Assert.Null(ConfigurationRanges.Violation(new ResilienceConfiguration()));
        Assert.Null(ConfigurationRanges.Violation(new ExternalServiceConfiguration()));
    }

    [Fact]
    public void The_first_violation_is_the_one_reported()
    {
        Assert.Equal(
            "instances must be 1 or greater.",
            ConfigurationRanges.Violation(new ApiConfiguration { Instances = 0, RequestsPerSecond = -1 }));
    }

    [Fact]
    public void A_zone_in_the_closed_set_passes()
    {
        Assert.Null(ConfigurationRanges.ZoneViolation(NodeType.Api, Parse("""{"zone":"a"}""")));
        Assert.Null(ConfigurationRanges.ZoneViolation(NodeType.Database, Parse("""{"zone":"b","replicas":1}""")));
        Assert.Null(ConfigurationRanges.ZoneViolation(NodeType.Observability, Parse("""{"zone":"c"}""")));
    }

    [Fact]
    public void A_zone_outside_the_closed_set_is_named()
    {
        Assert.Equal("zone must be one of a, b, c.", ConfigurationRanges.ZoneViolation(NodeType.Api, Parse("""{"zone":"d"}""")));
        Assert.Equal("zone must be one of a, b, c.", ConfigurationRanges.ZoneViolation(NodeType.Api, Parse("""{"zone":"A"}""")));
        Assert.Equal("zone must be one of a, b, c.", ConfigurationRanges.ZoneViolation(NodeType.Api, Parse("""{"zone":3}""")));
        Assert.Equal("zone must be one of a, b, c.", ConfigurationRanges.ZoneViolation(NodeType.Api, Parse("""{"zone":null}""")));
    }

    [Fact]
    public void A_zone_on_a_type_nobody_places_is_rejected()
    {
        Assert.Equal("zone does not apply to Cdn.", ConfigurationRanges.ZoneViolation(NodeType.Cdn, Parse("""{"zone":"a"}""")));
        Assert.Equal("zone does not apply to Client.", ConfigurationRanges.ZoneViolation(NodeType.Client, Parse("""{"zone":"a"}""")));
        Assert.Equal(
            "zone does not apply to ExternalService.",
            ConfigurationRanges.ZoneViolation(NodeType.ExternalService, Parse("""{"zone":"a"}""")));
    }

    [Fact]
    public void No_zone_declared_is_always_fine()
    {
        Assert.Null(ConfigurationRanges.ZoneViolation(NodeType.Api, Parse("{}")));
        Assert.Null(ConfigurationRanges.ZoneViolation(NodeType.Cdn, Parse("""{"hitRatio":0.9}""")));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}
