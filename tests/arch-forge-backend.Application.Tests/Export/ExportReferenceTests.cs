using arch_forge_backend.Application.Export;
using arch_forge_backend.Domain.Entities;
using Xunit;
using static arch_forge_backend.Application.Tests.Export.ExportFixtures;

namespace arch_forge_backend.Application.Tests.Export;

/// <summary>
/// V5 D3.3. The document is a <strong>pure and deterministic function of its inputs</strong>, so it
/// is tested with the same technique that produced the exact scores <c>100 · 83 · 96 · 65</c> and
/// the five reference scenarios: a golden file, compared whole.
///
/// None of this was possible with an integrated model, whose output does not repeat even for the
/// same input. It is where the whole direction of V5 pays for itself.
///
/// <strong>The whole document, not parts.</strong> A partial golden does not notice that an extra
/// section has crept in. The fragility is the correct price: changing a comma of the wording breaks
/// four tests, which is what has to happen when changing a contract that carries its own
/// <c>exportVersion</c> (D2.3).
/// </summary>
public class ExportReferenceTests
{
    [Theory]
    [InlineData("minimal-api-db")]
    [InlineData("worst-case")]
    [InlineData("resilient-async")]
    [InlineData("gateway-microservices")]
    public void The_document_matches_its_reference(string fixtureName)
    {
        var (nodes, connections) = Analysis.Fixtures.ReferenceArchitecture.Load(fixtureName);

        var architecture = new Architecture
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = fixtureName,
            ShareToken = "8fK29x",
            Version = 1,
            Nodes = nodes,
            Connections = connections
        };

        var document = MarkdownExporter.Compose(
            architecture,
            Analysis.Fixtures.ReferenceArchitecture.Analyze(fixtureName),
            ExportedAt);

        Assert.Equal(Reference(fixtureName), document, ignoreLineEndingDifferences: true);
    }

    /// <summary>
    /// D2.6, and the same job "no predicate reads the score" did in V4: without this the decision is
    /// an intention. The export tells what the user built, never what they were asked to build - a
    /// button that hands the statement over would let a model solve the challenge outright.
    /// </summary>
    [Theory]
    [InlineData("minimal-api-db")]
    [InlineData("worst-case")]
    [InlineData("resilient-async")]
    [InlineData("gateway-microservices")]
    public void The_document_mentions_no_challenge(string fixtureName)
    {
        var document = Reference(fixtureName);

        foreach (var slug in new[] { "payments-friday", "database-overload", "latency-budget", "million-a-day" })
            Assert.DoesNotContain(slug, document, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("challenge", document, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reto", document, StringComparison.OrdinalIgnoreCase);
    }

    private static string Reference(string fixtureName) =>
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Analysis", "Fixtures", "Architectures", $"{fixtureName}.export.md"));
}
