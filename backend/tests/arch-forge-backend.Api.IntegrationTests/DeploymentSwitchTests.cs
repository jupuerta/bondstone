using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace bondstone_backend.Api.IntegrationTests;

// The two switches S6 and S12 left behind. Neither is on in this deployment, which is exactly why
// they are pinned: a switch nobody exercises has quietly stopped working by the time it is needed.
//
// No container behind any of this, and that is the point of the third test rather than an economy
// measure: with migrations off, startup resolves the context but never opens a connection, so the
// API boots with nothing listening at the other end. A deployment that migrates as its own step
// gets to start its API without waiting for the database to be reachable — and the same fact makes
// these the only integration tests that cost nothing to run.
public abstract class SwitchFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Well-formed and deliberately dead: it proves the connection string guard is satisfied
        // (AddInfrastructure still refuses an empty one) without anything answering.
        builder.UseSetting(
            "ConnectionStrings:Bondstone",
            "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1");

        builder.UseSetting("Database:MigrateOnStartup", "false");
    }

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });
}

public class HttpsRequiredFactory : SwitchFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Security:RequireHttps", "true");
    }
}

public class HttpsRequiredTests(HttpsRequiredFactory factory) : IClassFixture<HttpsRequiredFactory>
{
    [Fact]
    public async Task Plain_http_is_redirected_rather_than_served()
    {
        var response = await factory.Client().GetAsync("/api/challenges");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https", response.Headers.Location!.Scheme);
    }

    // Two framework rules this respects rather than fights: HSTS goes out only on a request that
    // already arrived over HTTPS, and never to localhost, which is excluded so nobody pins their
    // own machine. Hence https, and a name that is not localhost — as every real request would be.
    [Fact]
    public async Task The_answer_carries_hsts_so_the_browser_stops_asking_in_clear()
    {
        var response = await factory.Client().GetAsync("https://bondstone.example/api/challenges");

        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }
}

public class PlainHttpFactory : SwitchFactory;

public class DefaultsTests(PlainHttpFactory factory) : IClassFixture<PlainHttpFactory>
{
    // The API comes up and serves what does not need the database, with no database there at all.
    // That is what lets migrations move out of startup, and it is also the default posture of this
    // deployment: no redirect, because the nginx in front of it speaks plain HTTP on the same host.
    [Fact]
    public async Task Without_startup_migrations_the_api_still_boots_and_serves()
    {
        var response = await factory.Client().GetAsync("/api/challenges");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
