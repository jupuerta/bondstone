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
        var createResponse = await _client.PostAsJsonAsync("/api/architectures", new { name = "Checkout flow" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonObject>();
        var id = created!["id"]!.GetValue<Guid>();
        var shareToken = created["shareToken"]!.GetValue<string>();
        Assert.Equal(1, created["version"]!.GetValue<int>());

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
                new { id = apiNodeId, type = "Api", name = "Api", positionX = 0, positionY = 0 },
                new { id = ordersNodeId, type = "Service", name = "Orders", positionX = 100, positionY = 0 },
                new { id = paymentsNodeId, type = "Service", name = "Payments", positionX = 200, positionY = 0 },
                new { id = bankNodeId, type = "Service", name = "Bank", positionX = 300, positionY = 0 }
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

        var analyzeResponse = await _client.PostAsync($"/api/architectures/{id}/analyze", content: null);
        Assert.Equal(HttpStatusCode.OK, analyzeResponse.StatusCode);
        var analysis = await analyzeResponse.Content.ReadFromJsonAsync<JsonObject>();
        var findings = analysis!["findings"]!.AsArray();
        Assert.Contains(findings, f => f!["code"]!.GetValue<string>() == "SYNC_CHAIN");
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
}
