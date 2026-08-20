using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace arch_forge_backend.Api.IntegrationTests;

/// <summary>
/// V4 VS1 over the wire: the loop a user actually walks. Start a challenge, get a real architecture
/// with a real share token, evaluate it as it comes, and get told exactly why it does not pass.
///
/// The evaluator itself is covered without HTTP in <c>Application.Tests/Challenges</c>. What these
/// add is the part only the wire can break: that starting persists an ordinary architecture (D2.2),
/// that the verdict travels whole, and that the two 404s are 404s.
/// </summary>
public class ChallengeEndpointTests : IClassFixture<PostgresApiFactory>
{
    private readonly HttpClient _client;

    public ChallengeEndpointTests(PostgresApiFactory factory)
    {
        _client = factory.CreateClient();
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
        // D3.6: without a ranking, building while looking at the checklist is learning, not
        // cheating - the checklist is the enunciado broken into pieces.
        var body = await _client.GetFromJsonAsync<JsonObject>("/api/challenges/payments-friday");

        Assert.NotNull(body);
        Assert.NotEmpty(body!["requirements"]!.AsArray());
        // V7 D1.1: a label is a catalogue key. The prose behind it lives in the client's
        // catalogue - the backend renders no challenge text - so what travels here is the pointer.
        Assert.All(body["requirements"]!.AsArray(), requirement =>
            Assert.StartsWith("challenge.payments-friday.", requirement!["label"]!["key"]!.GetValue<string>()));

        Assert.Equal("challenge.payments-friday.title", body["title"]!["key"]!.GetValue<string>());
        Assert.Equal("challenge.payments-friday.story", body["story"]!["key"]!.GetValue<string>());

        // And the reference solution is not reachable by guessing a path (D3.5).
        Assert.Null(body["referenceSolution"]);
        Assert.Null(body["starter"]);
    }

    [Fact]
    public async Task Starting_a_challenge_persists_an_ordinary_architecture()
    {
        var started = await StartAsync();

        // D2.2: an attempt is not a special kind of thing. It has a share token and it loads through
        // the same endpoint as anything else, which is why the client can hand it to /a/{token}.
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
        var id = started["id"]!.GetValue<string>();

        var response = await _client.PostAsJsonAsync("/api/challenges/payments-friday/evaluate", new { architectureId = id });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.False(body!["passed"]!.GetValue<bool>());

        // D3.3: whatever is not met carries a reason, and the elements the canvas lights up.
        var unmet = body["requirements"]!.AsArray().Where(r => !r!["met"]!.GetValue<bool>()).ToList();
        Assert.NotEmpty(unmet);
        Assert.All(unmet, requirement =>
            Assert.False(string.IsNullOrWhiteSpace(requirement!["detail"]!["key"]!.GetValue<string>())));
    }

    [Fact]
    public async Task Forking_gives_a_copy_with_its_own_token()
    {
        var started = await StartAsync();
        var id = started["id"]!.GetValue<string>();

        var response = await _client.PostAsync($"/api/architectures/{id}/fork", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var fork = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.NotEqual(id, fork!["id"]!.GetValue<string>());
        Assert.NotEqual(started["shareToken"]!.GetValue<string>(), fork["shareToken"]!.GetValue<string>());
        Assert.Equal(1, fork["version"]!.GetValue<int>());
        Assert.Equal(4, fork["nodes"]!.AsArray().Count);

        // Fresh ids for every node: they are primary keys, so two architectures cannot share them.
        // That is precisely why a challenge's faults point at components by name (D2.3).
        var originalIds = started["nodes"]!.AsArray().Select(n => n!["id"]!.GetValue<string>()).ToHashSet();
        Assert.All(fork["nodes"]!.AsArray(), node => Assert.DoesNotContain(node!["id"]!.GetValue<string>(), originalIds));
    }

    [Fact]
    public async Task An_unknown_slug_is_a_404_on_every_endpoint()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/challenges/nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/api/challenges/nope/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/api/challenges/nope/solution", null)).StatusCode);

        var evaluate = await _client.PostAsJsonAsync("/api/challenges/nope/evaluate", new { architectureId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, evaluate.StatusCode);
    }

    [Fact]
    public async Task Evaluating_an_architecture_that_is_gone_is_a_404()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/challenges/payments-friday/evaluate",
            new { architectureId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_reference_solution_opens_as_an_architecture_that_passes()
    {
        // D3.5 and D6.4: not a picture - an architecture with its own token, so it opens in the
        // editor next to the attempt and can be analysed, simulated and taken apart.
        // V7 D2.1: the name travels in the body, because the backend holds no text. Named so it
        // cannot be mistaken for the attempt it is being compared against — two tabs of the same
        // editor with the same title would be a trap.
        var response = await _client.PostAsJsonAsync(
            "/api/challenges/payments-friday/solution",
            new { name = "Los viernes — solución de referencia" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var solution = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal("Los viernes — solución de referencia", solution["name"]!.GetValue<string>());

        var evaluated = await _client.PostAsJsonAsync(
            "/api/challenges/payments-friday/evaluate",
            new { architectureId = solution["id"]!.GetValue<string>() });

        var verdict = await evaluated.Content.ReadFromJsonAsync<JsonObject>();
        Assert.True(verdict!["passed"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Opening_the_solution_gives_everyone_their_own_copy()
    {
        // Taking the author's solution apart must not change what the next person is shown.
        var first = await _client.PostAsync("/api/challenges/payments-friday/solution", null);
        var second = await _client.PostAsync("/api/challenges/payments-friday/solution", null);

        var a = (await first.Content.ReadFromJsonAsync<JsonObject>())!;
        var b = (await second.Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.NotEqual(a["id"]!.GetValue<string>(), b["id"]!.GetValue<string>());
        Assert.NotEqual(a["shareToken"]!.GetValue<string>(), b["shareToken"]!.GetValue<string>());
    }

    private async Task<JsonObject> StartAsync()
    {
        var response = await _client.PostAsync("/api/challenges/payments-friday/start", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    /// <summary>
    /// V7 D2.1. The name is the client's, and a call that sends none still gets a readable one —
    /// the slug — rather than an empty column or a catalogue key nobody can read.
    /// </summary>
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
