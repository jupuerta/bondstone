using System.Net;
using System.Text.Json.Nodes;
using Xunit;

namespace arch_forge_backend.Api.IntegrationTests;

/// <summary>
/// V16 C2 over the wire. What <c>ModelReference</c> publishes is covered without HTTP in
/// <c>Application.Tests/Simulation</c>; what only the wire can break is here — that the route
/// exists, and that a type arrives as <c>"Database"</c> rather than as the ordinal of an enum
/// whose declaration order is the canonical order of the graph (V6 D1.1). A client reading a
/// number where it expected a name would draw the wrong sixteen rows and say nothing.
/// </summary>
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

        // The three thresholds a screen quotes as figures rather than as prose.
        Assert.Equal(0.8m, (decimal)body["saturationWarning"]!);
        Assert.Equal(30_000, (int)body["unboundedWaitMs"]!);
        Assert.Equal(20m, (decimal)body["queueing"]!["cap"]!);
    }

    [Fact]
    public async Task It_is_read_only()
    {
        // No body, no token, nothing to persist: the model is the criterion the analyzer uses, and
        // there is no version of it that belongs to one architecture.
        var response = await _client.PostAsync("/api/model", content: null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
