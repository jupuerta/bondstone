using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using arch_forge_backend.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace arch_forge_backend.Api.IntegrationTests;

public class ChallengeEndpointTests : IClassFixture<PostgresApiFactory>
{
    private readonly HttpClient _client;
    private readonly PostgresApiFactory _factory;

    public ChallengeEndpointTests(PostgresApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<int> CountArchitecturesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ArchForgeDbContext>().Architectures.CountAsync();
    }

    [Fact]
    public async Task The_catalogue_lists_the_challenges()
    {
        var body = await _client.GetFromJsonAsync<JsonArray>("/api/challenges");

        Assert.NotNull(body);
        Assert.NotEmpty(body);
        Assert.Contains(body, item => item!["slug"]!.GetValue<string>() == "payments-friday");
    }

    [Fact]
    public async Task The_detail_carries_the_requirements_before_anything_is_evaluated()
    {
        var body = await _client.GetFromJsonAsync<JsonObject>("/api/challenges/payments-friday");

        Assert.NotNull(body);
        Assert.NotEmpty(body!["requirements"]!.AsArray());
        Assert.All(body["requirements"]!.AsArray(), requirement =>
            Assert.StartsWith("challenge.payments-friday.", requirement!["label"]!["key"]!.GetValue<string>()));

        Assert.Equal("challenge.payments-friday.title", body["title"]!["key"]!.GetValue<string>());
        Assert.Equal("challenge.payments-friday.story", body["story"]!["key"]!.GetValue<string>());

        Assert.Null(body["referenceSolution"]);
        Assert.Null(body["starter"]);
    }

    [Fact]
    public async Task Starting_a_challenge_persists_an_ordinary_architecture()
    {
        var started = await StartAsync();

        var reloaded = await _client.GetFromJsonAsync<JsonObject>(
            $"/api/architectures/{started["shareToken"]!.GetValue<string>()}");

        Assert.NotNull(reloaded);
        Assert.Equal(4, reloaded!["nodes"]!.AsArray().Count);
        Assert.Equal(3, reloaded["connections"]!.AsArray().Count);
    }

    [Fact]
    public async Task The_starter_does_not_pass_and_says_why()
    {
        var started = await StartAsync();
        var shareToken = started["shareToken"]!.GetValue<string>();

        var response = await _client.PostAsJsonAsync("/api/challenges/payments-friday/evaluate", new { shareToken });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.False(body!["passed"]!.GetValue<bool>());

        var unmet = body["requirements"]!.AsArray().Where(r => !r!["met"]!.GetValue<bool>()).ToList();
        Assert.NotEmpty(unmet);
        Assert.All(unmet, requirement =>
            Assert.False(string.IsNullOrWhiteSpace(requirement!["detail"]!["key"]!.GetValue<string>())));
    }

    [Fact]
    public async Task Forking_gives_a_copy_with_its_own_token()
    {
        var started = await StartAsync();
        var shareToken = started["shareToken"]!.GetValue<string>();

        var response = await _client.PostAsync($"/api/architectures/{shareToken}/fork", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var fork = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.NotEqual(started["editToken"]!.GetValue<string>(), fork!["editToken"]!.GetValue<string>());
        Assert.NotEqual(shareToken, fork["shareToken"]!.GetValue<string>());
        Assert.Equal(1, fork["version"]!.GetValue<int>());
        Assert.Equal(4, fork["nodes"]!.AsArray().Count);

        var originalIds = started["nodes"]!.AsArray().Select(n => n!["id"]!.GetValue<string>()).ToHashSet();
        Assert.All(fork["nodes"]!.AsArray(), node => Assert.DoesNotContain(node!["id"]!.GetValue<string>(), originalIds));
    }

    [Fact]
    public async Task An_unknown_slug_is_a_404_on_every_endpoint()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/challenges/nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/api/challenges/nope/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/api/challenges/nope/solution", null)).StatusCode);

        var evaluate = await _client.PostAsJsonAsync("/api/challenges/nope/evaluate", new { shareToken = Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.NotFound, evaluate.StatusCode);
    }

    [Fact]
    public async Task Evaluating_an_architecture_that_is_gone_is_a_404()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/challenges/payments-friday/evaluate",
            new { shareToken = Guid.NewGuid().ToString("N") });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_reference_solution_opens_as_an_architecture_that_passes()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/challenges/payments-friday/solution",
            new { name = "Los viernes — solución de referencia" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var solution = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal("Los viernes — solución de referencia", solution["name"]!.GetValue<string>());

        Assert.Null(solution["editToken"]);

        var evaluated = await _client.PostAsJsonAsync(
            "/api/challenges/payments-friday/evaluate",
            new { shareToken = solution["shareToken"]!.GetValue<string>() });

        var verdict = await evaluated.Content.ReadFromJsonAsync<JsonObject>();
        Assert.True(verdict!["passed"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Previewing_the_solution_says_the_same_as_opening_it()
    {
        var opened = (await (await _client.PostAsync("/api/challenges/payments-friday/solution", null))
            .Content.ReadFromJsonAsync<JsonObject>())!;

        var preview = (await (await _client.GetAsync("/api/challenges/payments-friday/solution/preview"))
            .Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.Equal(
            opened["nodes"]!.AsArray().Select(node => node!["name"]!.GetValue<string>()).OrderBy(name => name),
            preview["nodes"]!.AsArray().Select(node => node!["name"]!.GetValue<string>()).OrderBy(name => name));

        Assert.Equal(opened["connections"]!.AsArray().Count, preview["connections"]!.AsArray().Count);

        var analyzed = await (await _client.PostAsync(
            $"/api/architectures/{opened["shareToken"]!.GetValue<string>()}/analyze", null))
            .Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(
            analyzed!["score"]!.GetValue<int>(),
            preview["analysis"]!["score"]!.GetValue<int>());
    }

    [Fact]
    public async Task Previewing_the_solution_does_not_create_an_architecture()
    {
        var before = await CountArchitecturesAsync();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/challenges/payments-friday/solution/preview")).StatusCode);

        Assert.Equal(before, await CountArchitecturesAsync());

        await _client.PostAsync("/api/challenges/payments-friday/solution", null);
        Assert.Equal(before + 1, await CountArchitecturesAsync());
    }

    [Fact]
    public async Task Previewing_a_challenge_that_is_not_there_is_a_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/challenges/nope/solution/preview")).StatusCode);
    }

    [Fact]
    public async Task Opening_the_solution_gives_everyone_their_own_copy()
    {
        var first = await _client.PostAsync("/api/challenges/payments-friday/solution", null);
        var second = await _client.PostAsync("/api/challenges/payments-friday/solution", null);

        var a = (await first.Content.ReadFromJsonAsync<JsonObject>())!;
        var b = (await second.Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.NotEqual(a["shareToken"]!.GetValue<string>(), b["shareToken"]!.GetValue<string>());
    }

    private async Task<JsonObject> StartAsync()
    {
        var response = await _client.PostAsync("/api/challenges/payments-friday/start", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    [Fact]
    public async Task Starting_without_a_name_falls_back_to_the_slug()
    {
        var response = await _client.PostAsync("/api/challenges/payments-friday/start", null);
        var started = (await response.Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.Equal("payments-friday", started["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Starting_with_a_name_uses_it()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/challenges/payments-friday/start",
            new { name = "Tu servicio de pagos se cae todos los viernes" });

        var started = (await response.Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.Equal("Tu servicio de pagos se cae todos los viernes", started["name"]!.GetValue<string>());
    }
}
