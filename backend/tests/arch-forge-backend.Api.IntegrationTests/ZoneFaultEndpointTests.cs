using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace bondstone_backend.Api.IntegrationTests;

public class ZoneFaultEndpointTests : IClassFixture<PostgresApiFactory>
{
    private readonly HttpClient _client;

    public ZoneFaultEndpointTests(PostgresApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Simulate_DroppingAZone_TakesEverythingPlacedInItAndNothingElse()
    {
        var seed = await SeedTwoZonesAsync();

        var response = await _client.PostAsJsonAsync($"/api/architectures/{seed.ShareToken}/simulate", new
        {
            faults = Array.Empty<object>(),
            zones = new[] { "a" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Empty(body!["discardedFaults"]!.AsArray());

        var states = body["states"]!.AsArray();

        Assert.Equal("Down", StateOf(states, seed.ApiA));
        Assert.Equal("Healthy", StateOf(states, seed.ApiB));
        Assert.Equal("Healthy", StateOf(states, seed.Db));
    }

    [Fact]
    public async Task Simulate_AZoneNobodyIsIn_OpensInGreenRatherThanComplaining()
    {
        var seed = await SeedTwoZonesAsync();

        var response = await _client.PostAsJsonAsync($"/api/architectures/{seed.ShareToken}/simulate", new
        {
            faults = Array.Empty<object>(),
            zones = new[] { "c" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.All(body!["states"]!.AsArray(), state => Assert.Equal("Healthy", state!["state"]!.GetValue<string>()));
        Assert.Empty(body["discardedFaults"]!.AsArray());
    }

    [Fact]
    public async Task Simulate_AZoneOutsideTheClosedSet_ReturnsBadRequest()
    {
        var seed = await SeedTwoZonesAsync();

        var response = await _client.PostAsJsonAsync($"/api/architectures/{seed.ShareToken}/simulate", new
        {
            faults = Array.Empty<object>(),
            zones = new[] { "d" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Simulate_ABodyWithoutZones_StillWorksTheWayItAlwaysDid()
    {
        var seed = await SeedTwoZonesAsync();

        var response = await _client.PostAsJsonAsync($"/api/architectures/{seed.ShareToken}/simulate", new
        {
            faults = new[] { new { target = "Node", id = seed.Db, kind = "down" } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal("Down", StateOf(body!["states"]!.AsArray(), seed.Db));
    }

    private static string StateOf(JsonArray states, Guid id) =>
        states.Single(s => s!["type"]!.GetValue<string>() == "Node" && s["id"]!.GetValue<Guid>() == id)!
            ["state"]!.GetValue<string>();

    private async Task<(string ShareToken, Guid ApiA, Guid ApiB, Guid Db)> SeedTwoZonesAsync()
    {
        var created = await _client.PostAsJsonAsync("/api/architectures", new { name = "Zoned" });
        var architecture = await created.Content.ReadFromJsonAsync<JsonObject>();
        var editToken = architecture!["editToken"]!.GetValue<string>();
        var shareToken = architecture["shareToken"]!.GetValue<string>();

        var apiA = Guid.NewGuid();
        var apiB = Guid.NewGuid();
        var db = Guid.NewGuid();

        var update = await _client.PutAsJsonAsync($"/api/architectures/edit/{editToken}", new
        {
            name = "Zoned",
            version = 1,
            nodes = new object[]
            {
                new { id = apiA, type = "Api", name = "ApiA", position = new { x = 0, y = 0 }, configuration = new { zone = "a" } },
                new { id = apiB, type = "Api", name = "ApiB", position = new { x = 160, y = 0 }, configuration = new { zone = "b" } },
                new { id = db, type = "Database", name = "Db", position = new { x = 80, y = 160 } }
            },
            connections = new object[]
            {
                Edge(apiA, db),
                Edge(apiB, db)
            }
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        return (shareToken, apiA, apiB, db);
    }

    private static object Edge(Guid source, Guid target) => new
    {
        id = Guid.NewGuid(),
        sourceNodeId = source,
        targetNodeId = target,
        protocol = "Sql",
        synchronous = true,
        configuration = new { timeoutSeconds = 5 }
    };
}
