using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace arch_forge_backend.Api.IntegrationTests;

public class ArchitectureDeletionTests : IClassFixture<PostgresApiFactory>
{
    private readonly HttpClient _client;

    public ArchitectureDeletionTests(PostgresApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Delete_TakesTheArchitectureAndItsWholeGraph()
    {
        var (editToken, shareToken) = await SeedGraphAsync();

        var response = await _client.DeleteAsync($"/api/architectures/edit/{editToken}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/architectures/{shareToken}")).StatusCode);

        var orphaned = await _client.PostAsync($"/api/architectures/{shareToken}/analyze", null);
        Assert.Equal(HttpStatusCode.NotFound, orphaned.StatusCode);
    }

    [Fact]
    public async Task Delete_DoesNotNeedTheVersion_EvenAfterSomebodyElseEdited()
    {
        var (editToken, _) = await SeedGraphAsync();

        var edit = await _client.PutAsJsonAsync($"/api/architectures/edit/{editToken}", new
        {
            name = "Edited by somebody else",
            version = 2,
            nodes = Array.Empty<object>(),
            connections = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/architectures/edit/{editToken}")).StatusCode);
    }

    [Fact]
    public async Task Delete_Twice_ReturnsNotFoundTheSecondTime()
    {
        var (editToken, _) = await SeedGraphAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/architectures/edit/{editToken}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/architectures/edit/{editToken}")).StatusCode);
    }

    [Fact]
    public async Task Delete_UnknownEditToken_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"/api/architectures/edit/{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithAShareTokenInstead_ReturnsNotFound()
    {
        var (_, shareToken) = await SeedGraphAsync();

        var response = await _client.DeleteAsync($"/api/architectures/edit/{shareToken}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_LeavesEveryOtherArchitectureAlone()
    {
        var (doomedEditToken, _) = await SeedGraphAsync();
        var (_, survivorShareToken) = await SeedGraphAsync();

        await _client.DeleteAsync($"/api/architectures/edit/{doomedEditToken}");

        var reloaded = await _client.GetFromJsonAsync<JsonObject>($"/api/architectures/{survivorShareToken}");
        Assert.Equal(survivorShareToken, reloaded!["shareToken"]!.GetValue<string>());
        Assert.Equal(2, reloaded["nodes"]!.AsArray().Count);
        Assert.Single(reloaded["connections"]!.AsArray());
    }

    private async Task<(string EditToken, string ShareToken)> SeedGraphAsync()
    {
        var created = await _client.PostAsJsonAsync("/api/architectures", new { name = "Doomed" });
        var architecture = await created.Content.ReadFromJsonAsync<JsonObject>();
        var editToken = architecture!["editToken"]!.GetValue<string>();

        var api = Guid.NewGuid();
        var db = Guid.NewGuid();

        var update = await _client.PutAsJsonAsync($"/api/architectures/edit/{editToken}", new
        {
            name = "Doomed",
            version = 1,
            nodes = new object[]
            {
                new { id = api, type = "Api", name = "Api", position = new { x = 0, y = 0 } },
                new { id = db, type = "Database", name = "Db", position = new { x = 0, y = 120 } }
            },
            connections = new object[]
            {
                new { id = Guid.NewGuid(), sourceNodeId = api, targetNodeId = db, protocol = "Sql", synchronous = true }
            }
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        return (editToken, architecture["shareToken"]!.GetValue<string>());
    }
}
