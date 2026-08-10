using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace arch_forge_backend.Api.IntegrationTests;

public class ArchitectureFlowTests : IClassFixture<PostgresApiFactory>
{
    private readonly HttpClient _client;

    public ArchitectureFlowTests(PostgresApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task FullFlow_CreateGetUpdateConflictAnalyze_BehavesPerContract()
    {
        var (id, shareToken) = await CreateArchitectureAsync("Checkout flow");

        var getResponse = await _client.GetAsync($"/api/architectures/{shareToken}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var apiNodeId = Guid.NewGuid();
        var ordersNodeId = Guid.NewGuid();
        var paymentsNodeId = Guid.NewGuid();
        var bankNodeId = Guid.NewGuid();

        var updateBody = new
        {
            name = "Checkout flow",
            version = 1,
            nodes = new object[]
            {
                new { id = apiNodeId, type = "Api", name = "Api", position = new { x = 0, y = 0 } },
                new { id = ordersNodeId, type = "Service", name = "Orders", position = new { x = 100, y = 0 } },
                new { id = paymentsNodeId, type = "Service", name = "Payments", position = new { x = 200, y = 0 } },
                new { id = bankNodeId, type = "Service", name = "Bank", position = new { x = 300, y = 0 } }
            },
            connections = new object[]
            {
                new { id = Guid.NewGuid(), sourceNodeId = apiNodeId, targetNodeId = ordersNodeId, protocol = "Http", synchronous = true },
                new { id = Guid.NewGuid(), sourceNodeId = ordersNodeId, targetNodeId = paymentsNodeId, protocol = "Http", synchronous = true },
                new { id = Guid.NewGuid(), sourceNodeId = paymentsNodeId, targetNodeId = bankNodeId, protocol = "Http", synchronous = true }
            }
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/architectures/{id}", updateBody);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(2, updated!["version"]!.GetValue<int>());

        var staleResponse = await _client.PutAsJsonAsync($"/api/architectures/{id}", updateBody);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        var conflict = await staleResponse.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(2, conflict!["currentVersion"]!.GetValue<int>());

        var reloaded = await _client.GetFromJsonAsync<JsonObject>($"/api/architectures/{shareToken}");
        Assert.Equal(4, reloaded!["nodes"]!.AsArray().Count);
        Assert.Equal(3, reloaded["connections"]!.AsArray().Count);

        var analyzeResponse = await _client.PostAsync($"/api/architectures/{id}/analyze", content: null);
        Assert.Equal(HttpStatusCode.OK, analyzeResponse.StatusCode);
        var analysis = await analyzeResponse.Content.ReadFromJsonAsync<JsonObject>();
        var findings = analysis!["findings"]!.AsArray();
        Assert.Contains(findings, f => f!["code"]!.GetValue<string>() == "SYNC_CHAIN");
        Assert.True(analysis["score"]!.GetValue<int>() is >= 0 and <= 100);
    }

    [Fact]
    public async Task Put_RepeatedWithSameNodeIds_KeepsSavingAndBumpsVersion()
    {
        // The autosave loop re-sends the same client-generated ids on every flush.
        var (id, shareToken) = await CreateArchitectureAsync("Autosave");

        var apiNodeId = Guid.NewGuid();
        var databaseNodeId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();

        object Snapshot(int version, double x) => new
        {
            name = "Autosave",
            version,
            nodes = new object[]
            {
                new { id = apiNodeId, type = "Api", name = "Api", position = new { x, y = 0 } },
                new { id = databaseNodeId, type = "Database", name = "Db", position = new { x = 200, y = 0 } }
            },
            connections = new object[]
            {
                new { id = connectionId, sourceNodeId = apiNodeId, targetNodeId = databaseNodeId, protocol = "Sql", synchronous = true }
            }
        };

        for (var version = 1; version <= 4; version++)
        {
            var response = await _client.PutAsJsonAsync($"/api/architectures/{id}", Snapshot(version, version * 10));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<JsonObject>();
            Assert.Equal(version + 1, body!["version"]!.GetValue<int>());
        }

        var reloaded = await _client.GetFromJsonAsync<JsonObject>($"/api/architectures/{shareToken}");
        Assert.Equal(5, reloaded!["version"]!.GetValue<int>());
        Assert.Equal(2, reloaded["nodes"]!.AsArray().Count);
        Assert.Single(reloaded["connections"]!.AsArray());

        var movedNode = reloaded["nodes"]!.AsArray().Single(n => n!["id"]!.GetValue<Guid>() == apiNodeId);
        Assert.Equal(40, movedNode!["position"]!["x"]!.GetValue<double>());
    }

    [Fact]
    public async Task Put_NodeConfiguration_RoundTripsAsJsonObject()
    {
        var (id, shareToken) = await CreateArchitectureAsync("Configured");
        var queueNodeId = Guid.NewGuid();

        var response = await _client.PutAsJsonAsync($"/api/architectures/{id}", new
        {
            name = "Configured",
            version = 1,
            nodes = new object[]
            {
                new
                {
                    id = queueNodeId,
                    type = "Queue",
                    name = "Orders queue",
                    position = new { x = 10, y = 20 },
                    configuration = new { durable = true, deadLetterQueue = false, maxRetries = 3 }
                }
            },
            connections = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reloaded = await _client.GetFromJsonAsync<JsonObject>($"/api/architectures/{shareToken}");
        var configuration = reloaded!["nodes"]!.AsArray().Single()!["configuration"]!.AsObject();
        Assert.True(configuration["durable"]!.GetValue<bool>());
        Assert.Equal(3, configuration["maxRetries"]!.GetValue<int>());
    }

    [Fact]
    public async Task Put_NodeWithoutConfiguration_RoundTripsAsEmptyObject()
    {
        // §5.1: the column is jsonb NOT NULL, so an unconfigured node reads back as {}, not null.
        var (id, shareToken) = await CreateArchitectureAsync("Unconfigured");

        var response = await _client.PutAsJsonAsync($"/api/architectures/{id}", new
        {
            name = "Unconfigured",
            version = 1,
            nodes = new object[]
            {
                new { id = Guid.NewGuid(), type = "Api", name = "Api", position = new { x = 0, y = 0 } }
            },
            connections = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reloaded = await _client.GetFromJsonAsync<JsonObject>($"/api/architectures/{shareToken}");
        var configuration = reloaded!["nodes"]!.AsArray().Single()!["configuration"];

        Assert.NotNull(configuration);
        Assert.Empty(configuration!.AsObject());
    }

    [Fact]
    public async Task Put_ConfigurationThatDoesNotMatchSchema_ReturnsBadRequest()
    {
        var (id, _) = await CreateArchitectureAsync("Bad configuration");

        var response = await _client.PutAsJsonAsync($"/api/architectures/{id}", new
        {
            name = "Bad configuration",
            version = 1,
            nodes = new object[]
            {
                new
                {
                    id = Guid.NewGuid(),
                    type = "Queue",
                    name = "Orders queue",
                    position = new { x = 0, y = 0 },
                    configuration = new { durable = "not-a-boolean" }
                }
            },
            connections = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_ConnectionReferencingUnknownNode_ReturnsBadRequest()
    {
        var (id, _) = await CreateArchitectureAsync("Dangling edge");

        var response = await _client.PutAsJsonAsync($"/api/architectures/{id}", new
        {
            name = "Dangling edge",
            version = 1,
            nodes = Array.Empty<object>(),
            connections = new object[]
            {
                new
                {
                    id = Guid.NewGuid(),
                    sourceNodeId = Guid.NewGuid(),
                    targetNodeId = Guid.NewGuid(),
                    protocol = "Http",
                    synchronous = true
                }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_UnknownArchitecture_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync($"/api/architectures/{Guid.NewGuid()}", new
        {
            name = "Ghost",
            version = 1,
            nodes = Array.Empty<object>(),
            connections = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_UnknownArchitecture_ReturnsNotFound()
    {
        var response = await _client.PostAsync($"/api/architectures/{Guid.NewGuid()}/analyze", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_EmptyName_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/architectures", new { name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownShareToken_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/architectures/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(Guid Id, string ShareToken)> CreateArchitectureAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/architectures", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(1, created!["version"]!.GetValue<int>());

        return (created["id"]!.GetValue<Guid>(), created["shareToken"]!.GetValue<string>());
    }
}
