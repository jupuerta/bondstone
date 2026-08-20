using System.Text.RegularExpressions;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Export;
using arch_forge_backend.Application.Simulation;
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
    /// VS2. The fifth reference: §8.1 of V3 - <c>minimal-api-db · node:Db:down</c>, the smallest
    /// case - turned into a document. It freezes the simulation section and the Mermaid state
    /// classes, and its figures are verifiable against
    /// <c>ReferenceScenarioTests.MinimalApiDb_DbDown_TakesTheApiWithIt</c>, which was written two
    /// versions before this one and asserts every one of them.
    /// </summary>
    [Fact]
    public void The_simulated_document_matches_its_reference()
    {
        var (nodes, connections) = Analysis.Fixtures.ReferenceArchitecture.Load("minimal-api-db");
        var graph = new ArchitectureGraph(nodes, connections);

        var scenario = new SimulationScenario([
            new Fault(ElementType.Node, nodes.Single(n => n.Name == "Db").Id, FaultKinds.Down)
        ]);

        var architecture = new Architecture
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "minimal-api-db",
            ShareToken = "8fK29x",
            Version = 1,
            Nodes = nodes,
            Connections = connections
        };

        var document = MarkdownExporter.Compose(
            architecture,
            Analysis.Fixtures.ReferenceArchitecture.Analyze("minimal-api-db"),
            ExportedAt,
            new SimulationEngine().Run(graph, scenario),
            scenario);

        Assert.Equal(Reference("minimal-api-db.down"), document, ignoreLineEndingDifferences: true);
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

        // Whole words, not substrings: "concreto" contains "reto", and a naive check turns this
        // test into a trap for whoever next writes a sentence in the prompt.
        Assert.DoesNotMatch(new Regex(@"\b(challenges?|retos?|enunciados?)\b", RegexOptions.IgnoreCase), document);
    }

    private static string Reference(string fixtureName) =>
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Analysis", "Fixtures", "Architectures", $"{fixtureName}.export.md"));
}
