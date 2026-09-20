using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace bondstone_backend.Api.IntegrationTests;

// Its own factory, with a ceiling low enough to reach in a test. The shared one lifts the limits so
// the other 57 can mint freely, so this is the only place the refusal itself can be pinned.
public class TightMintLimitFactory : PostgresApiFactory
{
    public const int Permits = 3;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // `UseSetting` and not another in-memory source: the base factory already registers one for
        // this key, and which of two equal-precedence sources wins is not worth depending on.
        builder.UseSetting("RateLimiting:MintPermits", Permits.ToString());
    }
}

public class RateLimitEndpointTests(TightMintLimitFactory factory) : IClassFixture<TightMintLimitFactory>
{
    [Fact]
    public async Task Creating_architectures_stops_at_the_ceiling_and_says_why()
    {
        var client = factory.CreateClient();

        for (var i = 0; i < TightMintLimitFactory.Permits; i++)
        {
            var allowed = await client.PostAsJsonAsync("/api/architectures", new { name = $"probe {i}" });
            Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        }

        var refused = await client.PostAsJsonAsync("/api/architectures", new { name = "one too many" });

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.NotNull(refused.Headers.RetryAfter);

        // The frontend reads `message` off any body it has no typed shape for, so the refusal has to
        // carry one — otherwise the screen says "the request failed (429)".
        var body = await refused.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.False(string.IsNullOrWhiteSpace(body?["message"]));
    }

    [Fact]
    public async Task Reading_is_not_rationed_by_the_mint_limit()
    {
        var client = factory.CreateClient();

        // Well past the mint ceiling. Analysing, simulating and reading are what an editing session
        // does hundreds of times, and none of them costs a row.
        for (var i = 0; i < TightMintLimitFactory.Permits * 4; i++)
        {
            var response = await client.GetAsync("/api/challenges");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // It used to be exempt from every limiter; now it has a generous one of its own (S10), so what
    // this pins is the part that matters to the container: a burst far past the mint ceiling still
    // answers, because a probe that gets throttled reports the throttle as an outage.
    [Fact]
    public async Task The_health_probe_outlasts_the_mint_ceiling()
    {
        var client = factory.CreateClient();

        for (var i = 0; i < TightMintLimitFactory.Permits * 4; i++)
        {
            var response = await client.GetAsync("/api/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}

// No database behind it: the muzzle is middleware, so it answers before the health check ever asks
// anything. Whether the probe says healthy or not is beside the point — what is pinned is that the
// endpoint stops answering at all once the ceiling is met, which is what "no longer exempt" means.
public class MuzzledHealthFactory : SwitchFactory
{
    public const int Permits = 3;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimiting:HealthPermitsPerMinute", Permits.ToString());
    }
}

public class HealthCeilingTests(MuzzledHealthFactory factory) : IClassFixture<MuzzledHealthFactory>
{
    [Fact]
    public async Task The_probe_has_a_ceiling_of_its_own()
    {
        var client = factory.Client();

        for (var i = 0; i < MuzzledHealthFactory.Permits; i++)
        {
            var allowed = await client.GetAsync("/api/health");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        var refused = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }
}
