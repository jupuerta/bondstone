using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace arch_forge_backend.Api.IntegrationTests;

/// <summary>
/// V7 D1.2 over the wire. <c>/export</c> is the one endpoint that still answers with prose, so it
/// is the only one that has to be told which language to write in - and the only place where the
/// backend's own catalogue is exercised end to end.
///
/// The negotiation is deliberately forgiving: an unknown language gets the default rather than a
/// 400. A document in the wrong language is useful; a refused export is not.
/// </summary>
public class ExportLanguageTests : IClassFixture<PostgresApiFactory>
{
    private readonly HttpClient _client;

    public ExportLanguageTests(PostgresApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task With_no_header_the_document_comes_out_in_the_default_language()
    {
        var document = await ExportAsync(await SeedAsync(), acceptLanguage: null);

        Assert.Contains("## Análisis", document);
        Assert.Contains("| Nombre | Tipo | Configuración |", document);
    }

    [Fact]
    public async Task Accept_language_english_writes_the_whole_document_in_english()
    {
        var document = await ExportAsync(await SeedAsync(), "en-GB,en;q=0.9");

        // Its own headings...
        Assert.Contains("## Analysis", document);
        Assert.Contains("| Name | Type | Configuration |", document);

        // ...and the findings the analyzer produced, which is the half that used to be impossible.
        Assert.Contains("single point of failure", document);
        Assert.DoesNotContain("punto único de fallo", document);
    }

    [Fact]
    public async Task A_language_with_no_catalogue_falls_back_instead_of_failing()
    {
        var response = await _client.SendAsync(Request(await SeedAsync(), "de-DE,de;q=0.9"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("## Análisis", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The two documents say the same thing in two languages, so they have the same shape: same
    /// number of findings, same tables, same diagram. Only the words move.
    /// </summary>
    [Fact]
    public async Task The_two_languages_produce_documents_of_the_same_shape()
    {
        var id = await SeedAsync();

        var spanish = await ExportAsync(id, "es");
        var english = await ExportAsync(id, "en");

        Assert.Equal(Headings(spanish).Count, Headings(english).Count);
        Assert.NotEqual(spanish, english);
    }

    private static List<string> Headings(string document) => document
        .Split('\n')
        .Where(line => line.StartsWith("#", StringComparison.Ordinal))
        .ToList();

    private async Task<string> ExportAsync(Guid id, string? acceptLanguage)
    {
        var response = await _client.SendAsync(Request(id, acceptLanguage));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);

        return await response.Content.ReadAsStringAsync();
    }

    private static HttpRequestMessage Request(Guid id, string? acceptLanguage)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/architectures/{id}/export");

        if (acceptLanguage is not null)
            request.Headers.Add("Accept-Language", acceptLanguage);

        return request;
    }

    /// <summary>An Api and two Services hanging off one database, so SPOF has something to say.</summary>
    private async Task<Guid> SeedAsync()
    {
        var created = await _client.PostAsJsonAsync("/api/architectures", new { name = "Exported" });
        var id = (await created.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();

        var api = Guid.NewGuid();
        var orders = Guid.NewGuid();
        var billing = Guid.NewGuid();
        var db = Guid.NewGuid();

        var update = await _client.PutAsJsonAsync($"/api/architectures/{id}", new
        {
            name = "Exported",
            version = 1,
            nodes = new object[]
            {
                new { id = api, type = "Api", name = "Api", position = new { x = 0, y = 0 } },
                new { id = orders, type = "Service", name = "Orders", position = new { x = 0, y = 120 } },
                new { id = billing, type = "Service", name = "Billing", position = new { x = 160, y = 120 } },
                new { id = db, type = "Database", name = "Db", position = new { x = 80, y = 240 } }
            },
            connections = new object[]
            {
                Edge(api, orders),
                Edge(api, billing),
                Edge(orders, db, "Sql"),
                Edge(billing, db, "Sql")
            }
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        return id;
    }

    private static object Edge(Guid source, Guid target, string protocol = "Http") => new
    {
        id = Guid.NewGuid(),
        sourceNodeId = source,
        targetNodeId = target,
        protocol,
        synchronous = true,
        configuration = new { timeoutSeconds = 5 }
    };
}
