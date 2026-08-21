using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace arch_forge_backend.Api.IntegrationTests;

/// <summary>
/// V3 D2.4 over the wire. The distinction that matters here is the one D2.1 draws, because they are
/// two failures that look alike: an unknown kind is a malformed request and gets a 400, while an id
/// that no longer exists is an old link over an edited architecture and has to keep working.
/// </summary>
public class SimulationEndpointTests : IClassFixture<PostgresApiFactory>
{
    private readonly HttpClient _client;

    public SimulationEndpointTests(PostgresApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Simulate_KillingTheDatabase_TakesTheApiWithItAndReportsTheMetrics()
    {
        var (shareToken, api, db) = await SeedApiAndDatabaseAsync();

        var response = await _client.PostAsJsonAsync($"/api/architectures/{shareToken}/simulate", new
        {
            faults = new[] { new { target = "Node", id = db, kind = "down" } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal("v3", body!["simulationVersion"]!.GetValue<string>());

        var metrics = body["metrics"]!.AsObject();
        Assert.Equal(2, metrics["componentsDown"]!.GetValue<int>());
        Assert.Equal(27, metrics["nominalLatencyMs"]!.GetValue<int>());
        Assert.Equal(5005, metrics["faultedLatencyMs"]!.GetValue<int>());

        // Every element travels, healthy ones included: a green that is not in the contract cannot
        // be painted (D2.2). Two nodes and one edge here.
        Assert.Equal(3, body["states"]!.AsArray().Count);

        var apiState = body["states"]!.AsArray()
            .Single(s => s!["type"]!.GetValue<string>() == "Node" && s["id"]!.GetValue<Guid>() == api);

        Assert.Equal("Down", apiState!["state"]!.GetValue<string>());
        // V7 D1.1: the reason travels as a key and its arguments, not as a sentence. Asserting on
        // the key is asserting on the contract; the Spanish it used to check lived in a string the
        // panel no longer receives.
        Assert.Equal("sim.node.unprotected", apiState["reason"]!["key"]!.GetValue<string>());
        Assert.Equal("Db", apiState["reason"]!["args"]!["target"]!.GetValue<string>());
        Assert.Equal("Connection", apiState["causedBy"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Simulate_EmptyScenario_AnswersOkInGreen()
    {
        // D2.1: entering Simulate mode before choosing anything shows the architecture in green,
        // not an error and not an empty panel. It is also what /a/{token} serves without ?fault=.
        var (shareToken, _, _) = await SeedApiAndDatabaseAsync();

        var response = await _client.PostAsJsonAsync($"/api/architectures/{shareToken}/simulate", new { faults = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.All(body!["states"]!.AsArray(), state => Assert.Equal("Healthy", state!["state"]!.GetValue<string>()));
        Assert.Equal(0, body["metrics"]!["componentsDown"]!.GetValue<int>());
    }

    [Fact]
    public async Task Simulate_FaultOverAnElementThatIsGone_StillOpensAndSaysSo()
    {
        // D3.6: a link shared before the architecture was edited must keep working. Dropped, never
        // rejected - and announced, so the panel can say the experiment is not the one it was.
        var (shareToken, _, db) = await SeedApiAndDatabaseAsync();
        var ghost = Guid.NewGuid();

        var response = await _client.PostAsJsonAsync($"/api/architectures/{shareToken}/simulate", new
        {
            faults = new[]
            {
                new { target = "Node", id = ghost, kind = "down" },
                new { target = "Node", id = db, kind = "down" }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        var discarded = body!["discardedFaults"]!.AsArray();
        Assert.Single(discarded);
        Assert.Equal(ghost, discarded[0]!["id"]!.GetValue<Guid>());

        // And the rest of the experiment still ran.
        Assert.Equal(2, body["metrics"]!["componentsDown"]!.GetValue<int>());
    }

    [Fact]
    public async Task Simulate_UnknownFaultKind_ReturnsBadRequest()
    {
        // Front and back deploy together and a kind is never reused, so this can only come from a
        // malformed request - unlike a rule code, which V2 deliberately lets through (D2.1).
        var (shareToken, _, db) = await SeedApiAndDatabaseAsync();

        var response = await _client.PostAsJsonAsync($"/api/architectures/{shareToken}/simulate", new
        {
            faults = new[] { new { target = "Node", id = db, kind = "packetLoss" } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Simulate_TheSameFaultTwiceOverOneElement_ReturnsBadRequest()
    {
        var (shareToken, _, db) = await SeedApiAndDatabaseAsync();

        var response = await _client.PostAsJsonAsync($"/api/architectures/{shareToken}/simulate", new
        {
            faults = new[]
            {
                new { target = "Node", id = db, kind = "down" },
                new { target = "Node", id = db, kind = "down" }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Simulate_LatencyWithoutAValue_ReturnsBadRequest()
    {
        var (shareToken, _, db) = await SeedApiAndDatabaseAsync();

        var response = await _client.PostAsJsonAsync($"/api/architectures/{shareToken}/simulate", new
        {
            faults = new[] { new { target = "Node", id = db, kind = "latency" } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Simulate_UnknownArchitecture_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync($"/api/architectures/{Guid.NewGuid()}/simulate", new
        {
            faults = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>minimal-api-db over the wire: Api -> Db over SQL with a 5 s timeout.</summary>
    private async Task<(string ShareToken, Guid Api, Guid Db)> SeedApiAndDatabaseAsync()
    {
        var created = await _client.PostAsJsonAsync("/api/architectures", new { name = "Minimal" });
        var architecture = await created.Content.ReadFromJsonAsync<JsonObject>();
        var editToken = architecture!["editToken"]!.GetValue<string>();
        var shareToken = architecture["shareToken"]!.GetValue<string>();

        var api = Guid.NewGuid();
        var db = Guid.NewGuid();

        var update = await _client.PutAsJsonAsync($"/api/architectures/edit/{editToken}", new
        {
            name = "Minimal",
            version = 1,
            nodes = new object[]
            {
                new { id = api, type = "Api", name = "Api", position = new { x = 0, y = 0 } },
                new { id = db, type = "Database", name = "Db", position = new { x = 0, y = 120 } }
            },
            connections = new object[]
            {
                new
                {
                    id = Guid.NewGuid(),
                    sourceNodeId = api,
                    targetNodeId = db,
                    protocol = "Sql",
                    synchronous = true,
                    configuration = new { timeoutSeconds = 5 }
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        return (shareToken, api, db);
    }
}
