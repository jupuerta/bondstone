using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace bondstone_backend.Api.IntegrationTests;

// A ceiling of one byte, which any real database is already past. That makes "full" reachable in a
// test without writing two gigabytes to get there.
public class FullDatabaseFactory : PostgresApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Storage:MaxDatabaseBytes", "1");
        builder.UseSetting("Storage:MeasureEverySeconds", "0");
    }
}

public class StorageCeilingEndpointTests(FullDatabaseFactory factory) : IClassFixture<FullDatabaseFactory>
{
    [Fact]
    public async Task The_four_endpoints_that_create_a_row_refuse_when_there_is_no_room()
    {
        var client = factory.CreateClient();

        // All four, by name, because the ceiling is only worth anything if nothing that creates a
        // row is missing from it. A fifth one added later and not listed here is exactly the gap
        // this test exists to catch.
        var creators = new (string Name, Task<HttpResponseMessage> Call)[]
        {
            ("create", client.PostAsJsonAsync("/api/architectures", new { name = "no room" })),
            ("fork", client.PostAsync("/api/architectures/whatever/fork", null)),
            ("start a challenge", client.PostAsJsonAsync("/api/challenges/single-point-of-failure/start", new { name = "no room" })),
            ("publish a solution", client.PostAsJsonAsync("/api/challenges/single-point-of-failure/solution", new { name = "no room" })),
        };

        foreach (var (name, call) in creators)
        {
            var response = await call;

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.NotNull(response.Headers.RetryAfter);

            // The frontend reads `message` off any body it has no typed shape for.
            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            Assert.False(string.IsNullOrWhiteSpace(body?["message"]), $"«{name}» refused without saying why");
        }
    }

    [Fact]
    public async Task Everything_that_does_not_create_a_row_keeps_working()
    {
        var client = factory.CreateClient();

        // A full database is a reason to stop taking new work, not a reason to stop being a
        // website. Reading is how somebody finds the thing they are about to delete.
        var health = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var challenges = await client.GetAsync("/api/challenges");
        Assert.Equal(HttpStatusCode.OK, challenges.StatusCode);

        var missing = await client.GetAsync("/api/architectures/nosuchtoken");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Deleting_still_works_when_full()
    {
        // The one that matters most. If the way out of a full database were also closed, the only
        // way back would be somebody with an SSH key — and the people whose rows they are could do
        // nothing about it.
        var roomy = factory.WithWebHostBuilder(builder => builder.UseSetting("Storage:MaxDatabaseBytes", "0"));
        var created = await roomy.CreateClient()
            .PostAsJsonAsync("/api/architectures", new { name = "made while there was room" });
        var architecture = await created.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var editToken = architecture!["editToken"].ToString();

        var deleted = await factory.CreateClient().DeleteAsync($"/api/architectures/edit/{editToken}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }
}
