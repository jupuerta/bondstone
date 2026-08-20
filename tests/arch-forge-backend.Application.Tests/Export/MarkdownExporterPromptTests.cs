using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Export;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Enums;
using Xunit;
using static arch_forge_backend.Application.Tests.Export.ExportFixtures;

namespace arch_forge_backend.Application.Tests.Export;

/// <summary>
/// V5 VS3 and D2.5. The prompt is the <strong>only lever the product keeps over the quality of the
/// review</strong>, so it is worth more than a block of text at the end of a file looks like.
/// </summary>
public class MarkdownExporterPromptTests
{
    [Fact]
    public void The_document_ends_with_the_request_so_it_can_be_pasted_without_writing_anything()
    {
        var document = Composed();

        Assert.Contains("## Petición para la IA", document);
        Assert.EndsWith("Sé concreto y usa los nombres de los componentes.\n", document);
    }

    [Fact]
    public void Deleting_from_the_rule_onwards_leaves_a_report()
    {
        // The definition of "done" for VS3, and the reason the section sits last under a `---`:
        // whoever takes the document to a pull request deletes from there and still has a report.
        var document = Composed();
        var report = document[..document.LastIndexOf("\n---\n", StringComparison.Ordinal)];

        Assert.DoesNotContain("Petición para la IA", report);
        Assert.Contains("## Diagrama", report);
        Assert.Contains("## Componentes", report);
        Assert.Contains("## Análisis", report);

        // It ends on content, not on a dangling heading or a rule with nothing after it.
        var last = report.TrimEnd().Split('\n')[^1];
        Assert.False(last.StartsWith('#'), $"The report ends on a heading: '{last}'");
        Assert.NotEqual("---", last);
    }

    [Fact]
    public void The_request_names_no_rule_and_no_count_of_rules()
    {
        // D2.4 established that findings explain themselves, so the prompt has nothing to restate -
        // and a figure here would age exactly the way V4 aged the analyzer going from 7 rules to 15.
        var request = Request(Composed());

        foreach (var code in typeof(RuleCodes).GetFields().Select(f => (string)f.GetRawConstantValue()!))
            Assert.DoesNotContain(code, request, StringComparison.OrdinalIgnoreCase);

        foreach (var count in new[] { "siete", "quince", "dieciséis", " 7 ", " 15 ", " 16 " })
            Assert.DoesNotContain(count, request, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_request_is_identical_whether_there_is_a_simulation_or_not()
    {
        // One fixed text rather than a conditional one: it refers to the simulation as something
        // that may or may not be there, which keeps the section - and its golden - single.
        var api = Node("Api", NodeType.Api);
        var db = Node("Db", NodeType.Database);
        var architecture = Architecture(nodes: [api, db], connections: [Connection(api, db, ProtocolType.Sql)]);
        var analysis = Analyze(architecture);

        var graph = new ArchitectureGraph(architecture.Nodes, architecture.Connections);
        var simulation = new SimulationEngine().Run(graph, SimulationScenario.Empty);

        Assert.Equal(
            Request(MarkdownExporter.Compose(architecture, analysis, ExportedAt, TestText.Spanish)),
            Request(MarkdownExporter.Compose(architecture, analysis, ExportedAt, TestText.Spanish, simulation, SimulationScenario.Empty)));
    }

    private static string Composed()
    {
        var api = Node("Api", NodeType.Api);
        var db = Node("Db", NodeType.Database);
        var architecture = Architecture(nodes: [api, db], connections: [Connection(api, db, ProtocolType.Sql)]);

        return MarkdownExporter.Compose(architecture, Analyze(architecture), ExportedAt, TestText.Spanish);
    }

    private static string Request(string document) =>
        document[document.LastIndexOf("## Petición para la IA", StringComparison.Ordinal)..];
}
