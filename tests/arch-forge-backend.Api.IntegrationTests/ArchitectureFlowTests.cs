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
        var (editToken, shareToken) = await CreateArchitectureAsync("Checkout flow");

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

        var updateResponse = await _client.PutAsJsonAsync($"/api/architectures/edit/{editToken}", updateBody);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(2, updated!["version"]!.GetValue<int>());

        var staleResponse = await _client.PutAsJsonAsync($"/api/architectures/edit/{editToken}", updateBody);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        var conflict = await staleResponse.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(2, conflict!["currentVersion"]!.GetValue<int>());

        var reloaded = await _client.GetFromJsonAsync<JsonObject>($"/api/architectures/{shareToken}");
        Assert.Equal(4, reloaded!["nodes"]!.AsArray().Count);
        Assert.Equal(3, reloaded["connections"]!.AsArray().Count);

        var analyzeResponse = await _client.PostAsync($"/api/architectures/{shareToken}/analyze", content: null);
        Assert.Equal(HttpStatusCode.OK, analyzeResponse.StatusCode);
        var analysis = await analyzeResponse.Content.ReadFromJsonAsync<JsonObject>();
        var findings = analysis!["findings"]!.AsArray();
        Assert.Contains(findings, f => f!["code"]!.GetValue<string>() == "SYNC_CHAIN");
        Assert.True(analysis["score"]!.GetValue<int>() is >= 0 and <= 100);

        Assert.Equal("v10", analysis["scoringVersion"]!.GetValue<string>());
        Assert.Equal(
            ["Availability", "Resilience", "Coupling", "Scalability", "Security", "Observability", "Cost"],
            analysis["areas"]!.AsArray().Select(a => a!["area"]!.GetValue<string>()));
        Assert.Equal("NotApplicable", analysis["areas"]!.AsArray()
            .Single(a => a!["area"]!.GetValue<string>() == "Security")!["state"]!.GetValue<string>());
        Assert.DoesNotContain(
            analysis["areas"]!.AsArray(),
            a => a!["state"]!.GetValue<string>() == "NoRules");
        Assert.NotNull(analysis["summary"]);
    }

    [Fact]
    public async Task Put_ConnectionConfiguration_RoundTripsAsJsonObject()
    {
        var (editToken, shareToken) = await CreateArchitectureAsync("Resilient edge");
        var apiNodeId = Guid.NewGuid();
        var databaseNodeId = Guid.NewGuid();

        var response = await _client.PutAsJsonAsync($"/api/architectures/edit/{editToken}", new
        {
            name = "Resilient edge",
            version = 1,
            nodes = new object[]
            {
                new { id = apiNodeId, type = "Api", name = "Api", position = new { x = 0, y = 0 } },
                new { id = databaseNodeId, type = "Database", name = "Db", position = new { x = 200, y = 0 } }
            },
            connections = new object[]
            {
                new
                {
                    id = Guid.NewGuid(),
                    sourceNodeId = apiNodeId,
                    targetNodeId = databaseNodeId,
                    protocol = "Sql",
                    synchronous = true,
                    configuration = new { timeoutSeconds = 5, retries = 2, circuitBreaker = true, fallback = false }
                }
            }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reloaded = await _client.GetFromJsonAsync<JsonObject>($"/api/architectures/{shareToken}");
        var configuration = reloaded!["connections"]!.AsArray().Single()!["configuration"]!;

        Assert.Equal(5, configuration["timeoutSeconds"]!.GetValue<int>());
        Assert.Equal(2, configuration["retries"]!.GetValue<int>());
        Assert.True(configuration["circuitBreaker"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Put_ConnectionWithoutConfiguration_RoundTripsAsEmptyObject()
    {
        var (editToken, shareToken) = await CreateArchitectureAsync("Bare edge");
        var apiNodeId = Guid.NewGuid();
        var databaseNodeId = Guid.NewGuid();

        var response = await _client.PutAsJsonAsync($"/api/architectures/edit/{editToken}", new
        {
            name = "Bare edge",
            version = 1,
            nodes = new object[]
            {
                new { id = apiNodeId, type = "Api", name = "Api", position = new { x = 0, y = 0 } },
                new { id = databaseNodeId, type = "Database", name = "Db", position = new { x = 200, y = 0 } }
            },
            connections = new object[]
            {
                new { id = Guid.NewGuid(), sourceNodeId = apiNodeId, targetNodeId = databaseNodeId, protocol = "Sql", synchronous = true }
            }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reloaded = await _client.GetFromJsonAsync<JsonObject>($"/api/architectures/{shareToken}");

        Assert.Empty(reloaded!["connections"]!.AsArray().Single()!["configuration"]!.AsObject());
    }

    [Fact]
    public async Task Put_SelfLoop_ReturnsBadRequest()
    {
        var (editToken, _) = await CreateArchitectureAsync("Self loop");
        var nodeId = Guid.NewGuid();

        var response = await _client.PutAsJsonAsync($"/api/architectures/edit/{editToken}", new
        {
            name = "Self loop",
            version = 1,
            nodes = new object[]
            {
                new { id = nodeId, type = "Service", name = "Orders", position = new { x = 0, y = 0 } }
            },
            connections = new object[]
            {
                new { id = Guid.NewGuid(), sourceNodeId = nodeId, targetNodeId = nodeId, protocol = "Http", synchronous = true }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_GatewayAndLoadBalancerNodes_RoundTrip()
    {
        var (editToken, shareToken) = await CreateArchitectureAsync("Edge tier");

        var response = await _client.PutAsJsonAsync($"/api/architectures/edit/{editToken}", new
        {
            name = "Edge tier",
            version = 1,
            nodes = new object[]
            {
                new { id = Guid.NewGuid(), type = "Gateway", name = "Gateway", position = new { x = 0, y = 0 } },
                new
                {
                    id = Guid.NewGuid(),
                    type = "LoadBalancer",
                    name = "LB",
                    position = new { x = 200, y = 0 },
                    configuration = new { algorithm = "round-robin", healthCheck = true, instances = 2 }
                }
            },
            connections = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reloaded = await _client.GetFromJsonAsync<JsonObject>($"/api/architectures/{shareToken}");

        Assert.Equal(
            ["Gateway", "LoadBalancer"],
            reloaded!["nodes"]!.AsArray().Select(n => n!["type"]!.GetValue<string>()).Order());
    }

    [Fact]
    public async Task Put_RepeatedWithSameNodeIds_KeepsSavingAndBumpsVersion()
    {
        var (editToken, shareToken) = await CreateArchitectureAsync("Autosave");

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
            var response = await _client.PutAsJsonAsync($"/api/architectures/edit/{editToken}", Snapshot(version, version * 10));
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
        var (editToken, shareToken) = await CreateArchitectureAsync("Configured");
        var queueNodeId = Guid.NewGuid();

        var response = await _client.PutAsJsonAsync($"/api/architectures/edit/{editToken}", new
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
        var (editToken, shareToken) = await CreateArchitectureAsync("Unconfigured");

        var response = await _client.PutAsJsonAsync($"/api/architectures/edit/{editToken}", new
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
        var (editToken, _) = await CreateArchitectureAsync("Bad configuration");

        var response = await _client.PutAsJsonAsync($"/api/architectures/edit/{editToken}", new
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
                    configuration = new { maxRetries = "not-a-number" }
                }
            },
            connections = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_ConnectionReferencingUnknownNode_ReturnsBadRequest()
    {
        var (editToken, _) = await CreateArchitectureAsync("Dangling edge");

        var response = await _client.PutAsJsonAsync($"/api/architectures/edit/{editToken}", new
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
    public async Task Put_UnknownEditToken_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync($"/api/architectures/edit/{Guid.NewGuid():N}", new
        {
            name = "Ghost",
            version = 1,
            nodes = Array.Empty<object>(),
            connections = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_UnknownShareToken_ReturnsNotFound()
    {
        var response = await _client.PostAsync($"/api/architectures/{Guid.NewGuid():N}/analyze", content: null);

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

    private async Task<(string EditToken, string ShareToken)> CreateArchitectureAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/architectures", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(1, created!["version"]!.GetValue<int>());

        return (created["editToken"]!.GetValue<string>(), created["shareToken"]!.GetValue<string>());
    }

    [Fact]
    public async Task History_RecordsOneEntryPerSavedVersion()
    {
        var created = await _client.PostAsJsonAsync("/api/architectures", new { name = "History" });
        var architecture = (await created.Content.ReadFromJsonAsync<JsonObject>())!;
        var editToken = architecture["editToken"]!.GetValue<string>();
        var shareToken = architecture["shareToken"]!.GetValue<string>();

        var api = Guid.NewGuid();
        var db = Guid.NewGuid();

        object Snapshot(int version, bool fixedUp) => new
        {
            name = "History",
            version,
            nodes = new object[]
            {
                new
                {
                    id = api,
                    type = "Api",
                    name = "Api",
                    position = new { x = 0, y = 0 },
                    configuration = fixedUp ? new { authentication = true } : null
                },
                new { id = db, type = "Database", name = "Db", position = new { x = 200, y = 0 } }
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
                    configuration = fixedUp ? new { timeoutSeconds = 5 } : null
                }
            }
        };

        await _client.PutAsJsonAsync($"/api/architectures/edit/{editToken}", Snapshot(1, fixedUp: false));
        await _client.PutAsJsonAsync($"/api/architectures/edit/{editToken}", Snapshot(2, fixedUp: true));

        var history = await _client.GetFromJsonAsync<JsonArray>($"/api/architectures/{shareToken}/history");

        Assert.NotNull(history);
        Assert.Equal(2, history!.Count);

        Assert.Equal(2, history[0]!["version"]!.GetValue<int>());
        Assert.Equal(3, history[1]!["version"]!.GetValue<int>());
        Assert.All(history, entry => Assert.Equal("v10", entry!["scoringVersion"]!.GetValue<string>()));

        Assert.True(history[1]!["score"]!.GetValue<int>() > history[0]!["score"]!.GetValue<int>());

        var before = history[0]!["areas"]!.AsObject();
        var after = history[1]!["areas"]!.AsObject();

        Assert.True(after["Resilience"]!.GetValue<int>() > before["Resilience"]!.GetValue<int>());
        Assert.True(after["Security"]!.GetValue<int>() > before["Security"]!.GetValue<int>());

        Assert.Equal(before["Coupling"]!.GetValue<int>(), after["Coupling"]!.GetValue<int>());
    }

    [Fact]
    public async Task History_OfSomethingThatDoesNotExist_Is404()
    {
        var response = await _client.GetAsync($"/api/architectures/{Guid.NewGuid():N}/history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
