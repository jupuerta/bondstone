using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace arch_forge_backend.Api.IntegrationTests;

public class SweepEndpointTests : IClassFixture<PostgresApiFactory>
{
    private readonly HttpClient _client;

    public SweepEndpointTests(PostgresApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Fragility_AnswersOneRowPerComponentOrderedByBlastRadius()
    {
        var (shareToken, api, db, edge) = await SeedApiAndDatabaseAsync();

        var response = await _client.PostAsync($"/api/architectures/{shareToken}/fragility", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(2, body!["componentsTotal"]!.GetValue<int>());
        Assert.Equal(1, body["entryPointsTotal"]!.GetValue<int>());
        Assert.Equal(27, body["nominalLatencyMs"]!.GetValue<int>());

        var rows = body["rows"]!.AsArray();
        Assert.Equal(2, rows.Count);

        var falling = rows.Single(r => r!["nodeId"]!.GetValue<Guid>() == db)!.AsObject();

        Assert.Equal(2, falling["componentsDown"]!.GetValue<int>());
        Assert.Equal(0, falling["componentsDegraded"]!.GetValue<int>());
        Assert.Equal(1m, falling["blastRadius"]!.GetValue<decimal>());
        Assert.Equal(1, falling["entryPointsAffected"]!.GetValue<int>());
        Assert.Equal(1m, falling["requestsAffected"]!.GetValue<decimal>());
        Assert.Equal(5005, falling["faultedLatencyMs"]!.GetValue<int>());

        var down = falling["down"]!.AsArray()
            .Select(element => (element!["type"]!.GetValue<string>(), element["id"]!.GetValue<Guid>()))
            .ToHashSet();

        Assert.Contains(("Node", api), down);
        Assert.Contains(("Node", db), down);
        Assert.Contains(("Connection", edge), down);
        Assert.Empty(falling["degraded"]!.AsArray());
    }

    [Fact]
    public async Task Fragility_UnknownArchitecture_ReturnsNotFound()
    {
        var response = await _client.PostAsync($"/api/architectures/{Guid.NewGuid()}/fragility", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LoadCurve_AnswersTheWholeLadderAndTheBreakPoint()
    {
        var (shareToken, _, db, _) = await SeedApiAndDatabaseAsync(rps: 100);

        var response = await _client.PostAsync($"/api/architectures/{shareToken}/load-curve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        var points = body!["points"]!.AsArray();
        var ladder = points.Select(point => point!["scale"]!.GetValue<decimal>()).ToList();

        Assert.Equal(14, ladder.Count);
        Assert.Equal(1m, ladder[0]);
        Assert.Equal(20m, ladder[^1]);

        var first = points[0]!.AsObject();
        Assert.Equal(1m, first["scale"]!.GetValue<decimal>());
        Assert.Equal(100m, first["enteringRps"]!.GetValue<decimal>());
        Assert.Equal(48, first["nominalLatencyMs"]!.GetValue<int>());
        Assert.Equal(0m, first["backlogRps"]!.GetValue<decimal>());
        Assert.Equal(1m, first["loadAmplification"]!.GetValue<decimal>());
        Assert.Equal(2, first["components"]!.AsArray().Count);

        var breakPoint = body["breakPoint"]!.AsObject();
        Assert.Equal(1.6m, breakPoint["scale"]!.GetValue<decimal>());
        Assert.Equal(db, breakPoint["nodeId"]!.GetValue<Guid>());
        Assert.Equal(160m, breakPoint["enteringRps"]!.GetValue<decimal>());
        Assert.Equal(0.8m, breakPoint["utilization"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task LoadCurve_WithNothingDeclared_HasNoMetersAndNoBreakPoint()
    {
        var (shareToken, _, _, _) = await SeedApiAndDatabaseAsync();

        var response = await _client.PostAsync($"/api/architectures/{shareToken}/load-curve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Null(body!["breakPoint"]);
        Assert.All(body["points"]!.AsArray(), point => Assert.Empty(point!["components"]!.AsArray()));
    }

    [Fact]
    public async Task LoadCurve_DoesNotAnswerFindingsNorScore()
    {
        var (shareToken, _, _, _) = await SeedApiAndDatabaseAsync(rps: 100);

        var response = await _client.PostAsync($"/api/architectures/{shareToken}/load-curve", null);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.DoesNotContain(body!, property => property.Key is "findings" or "score" or "areas" or "summary");
    }

    [Fact]
    public async Task LoadCurve_UnknownArchitecture_ReturnsNotFound()
    {
        var response = await _client.PostAsync($"/api/architectures/{Guid.NewGuid()}/load-curve", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheSweepsDoNotVersionTheArchitecture()
    {
        var (shareToken, _, _, _) = await SeedApiAndDatabaseAsync(rps: 100);

        var before = await _client.GetFromJsonAsync<JsonArray>($"/api/architectures/{shareToken}/history");
        var version = (await _client.GetFromJsonAsync<JsonObject>($"/api/architectures/{shareToken}"))!["version"]!.GetValue<int>();

        await _client.PostAsync($"/api/architectures/{shareToken}/fragility", null);
        await _client.PostAsync($"/api/architectures/{shareToken}/load-curve", null);

        var after = await _client.GetFromJsonAsync<JsonArray>($"/api/architectures/{shareToken}/history");
        var architecture = await _client.GetFromJsonAsync<JsonObject>($"/api/architectures/{shareToken}");

        Assert.Equal(before!.Count, after!.Count);
        Assert.Equal(version, architecture!["version"]!.GetValue<int>());
    }

    private async Task<(string ShareToken, Guid Api, Guid Db, Guid Edge)> SeedApiAndDatabaseAsync(int? rps = null)
    {
        var created = await _client.PostAsJsonAsync("/api/architectures", new { name = "Minimal" });
        var architecture = await created.Content.ReadFromJsonAsync<JsonObject>();
        var editToken = architecture!["editToken"]!.GetValue<string>();
        var shareToken = architecture["shareToken"]!.GetValue<string>();

        var api = Guid.NewGuid();
        var db = Guid.NewGuid();
        var edge = Guid.NewGuid();

        var update = await _client.PutAsJsonAsync($"/api/architectures/edit/{editToken}", new
        {
            name = "Minimal",
            version = 1,
            nodes = new object[]
            {
                new
                {
                    id = api,
                    type = "Api",
                    name = "Api",
                    position = new { x = 0, y = 0 },
                    configuration = rps is null ? new { } : (object)new { requestsPerSecond = rps.Value }
                },
                new { id = db, type = "Database", name = "Db", position = new { x = 0, y = 120 } }
            },
            connections = new object[]
            {
                new
                {
                    id = edge,
                    sourceNodeId = api,
                    targetNodeId = db,
                    protocol = "Sql",
                    synchronous = true,
                    configuration = new { timeoutSeconds = 5 }
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        return (shareToken, api, db, edge);
    }
}
