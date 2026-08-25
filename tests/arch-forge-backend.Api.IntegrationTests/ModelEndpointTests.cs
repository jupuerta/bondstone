using System.Net;
using System.Text.Json.Nodes;
using Xunit;

namespace arch_forge_backend.Api.IntegrationTests;

public class ModelEndpointTests : IClassFixture<PostgresApiFactory>
{
    private readonly HttpClient _client;

    public ModelEndpointTests(PostgresApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task The_model_travels_whole_and_by_name()
    {
        var response = await _client.GetAsync("/api/model");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        var components = body["components"]!.AsArray();

        Assert.Equal(16, components.Count);

        var database = Assert.Single(components, row => (string?)row!["type"] == "Database");
        Assert.Equal(200, (int)database!["capacityPerInstance"]!);
        Assert.Equal(20, (int)database["processingMs"]!);

        Assert.Equal(0.8m, (decimal)body["saturationWarning"]!);
        Assert.Equal(30_000, (int)body["unboundedWaitMs"]!);
        Assert.Equal(20m, (decimal)body["queueing"]!["cap"]!);
    }

    [Fact]
    public async Task It_is_read_only()
    {
        var response = await _client.PostAsync("/api/model", content: null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
