using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace arch_forge_backend.Api.IntegrationTests;

/// <summary>
/// The only tests that check the thing that actually does the work: the cascade lives in the
/// database, so a unit test with a fake repository can only assert the intent. Here the rows go.
/// </summary>
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
        var (id, shareToken) = await SeedGraphAsync();

        var response = await _client.DeleteAsync($"/api/architectures/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Gone through both handles: the id the writes use and the token the link uses.
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/architectures/{shareToken}")).StatusCode);

        // And the nodes and connections went with it. If the cascade were not declared, this would
        // be a foreign key violation on the DELETE rather than a clean 404 here.
        var orphaned = await _client.PostAsync($"/api/architectures/{id}/analyze", null);
        Assert.Equal(HttpStatusCode.NotFound, orphaned.StatusCode);
    }

    [Fact]
    public async Task Delete_DoesNotNeedTheVersion_EvenAfterSomebodyElseEdited()
    {
        // Unlike the PUT, which answers 409 to a stale snapshot. A delete has nothing to merge:
        // the intent is "this should not exist", and somebody having moved a box a second earlier
        // does not make it less true.
        var (id, _) = await SeedGraphAsync();

        var edit = await _client.PutAsJsonAsync($"/api/architectures/{id}", new
        {
            name = "Edited by somebody else",
            version = 2,
            nodes = Array.Empty<object>(),
            connections = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/architectures/{id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_Twice_ReturnsNotFoundTheSecondTime()
    {
        var (id, _) = await SeedGraphAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/architectures/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/architectures/{id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_UnknownArchitecture_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"/api/architectures/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_LeavesEveryOtherArchitectureAlone()
    {
        var (doomed, _) = await SeedGraphAsync();
        var (survivor, survivorToken) = await SeedGraphAsync();

        await _client.DeleteAsync($"/api/architectures/{doomed}");

        var reloaded = await _client.GetFromJsonAsync<JsonObject>($"/api/architectures/{survivorToken}");
        Assert.Equal(survivor, reloaded!["id"]!.GetValue<Guid>());
        Assert.Equal(2, reloaded["nodes"]!.AsArray().Count);
        Assert.Single(reloaded["connections"]!.AsArray());
    }

    private async Task<(Guid Id, string ShareToken)> SeedGraphAsync()
    {
        var created = await _client.PostAsJsonAsync("/api/architectures", new { name = "Doomed" });
        var architecture = await created.Content.ReadFromJsonAsync<JsonObject>();
        var id = architecture!["id"]!.GetValue<Guid>();

        var api = Guid.NewGuid();
        var db = Guid.NewGuid();

        var update = await _client.PutAsJsonAsync($"/api/architectures/{id}", new
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
        return (id, architecture["shareToken"]!.GetValue<string>());
    }
}
