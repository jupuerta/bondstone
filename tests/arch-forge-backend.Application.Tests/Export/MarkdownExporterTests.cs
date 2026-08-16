using System.Text.RegularExpressions;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Export;
using arch_forge_backend.Domain.Enums;
using Xunit;
using static arch_forge_backend.Application.Tests.Export.ExportFixtures;

namespace arch_forge_backend.Application.Tests.Export;

public class MarkdownExporterTests
{
    // ---- Header -----------------------------------------------------------

    [Fact]
    public void The_header_carries_the_scoring_version_and_the_export_version()
    {
        // D2.3. A document that says "puntuación 83" without saying which engine produced it is a
        // figure with no meaning in six months - and this document is meant to live outside the app.
        var architecture = Architecture(name: "Orders Platform", shareToken: "8fK29x", version: 7);

        var document = MarkdownExporter.Compose(architecture, Analyze(architecture), ExportedAt);

        Assert.Contains("# Orders Platform", document);
        Assert.Contains("`8fK29x` · versión 7", document);
        Assert.Contains("2026-08-16", document);
        Assert.Contains($"`scoringVersion: {AnalysisResult.CurrentScoringVersion}`", document);
        Assert.Contains($"`exportVersion: {ExportVersion.Current}`", document);
    }

    // ---- Ajuste 1: ordering -----------------------------------------------

    [Fact]
    public void Shuffling_nodes_and_connections_does_not_change_a_single_character()
    {
        // The test that holds up ajuste 1. Without it the golden files are decorative: the
        // repository does not guarantee an order, so the same graph could produce two documents.
        //
        // The SAME AnalysisResult is composed twice, which is what the exporter owns. Re-analyzing
        // the shuffled graph would test something else and would fail for a reason that predates
        // V5: `Order` in ArchitectureAnalyzer breaks ties on (Severity, Area, Code) and nothing
        // else, so two findings sharing all three keep the order the rules emitted them in - which
        // is the input order of `graph.Nodes`. See plan técnico §2, ajuste 5.
        var gateway = Node("Edge", NodeType.Gateway);
        var api = Node("Orders API", NodeType.Api);
        var payments = Node("Payments", NodeType.Service);
        var db = Node("Orders DB", NodeType.Database);

        var edges = new[]
        {
            Connection(gateway, api),
            Connection(api, payments),
            Connection(api, db, ProtocolType.Sql)
        };

        var straight = Architecture(nodes: [gateway, api, payments, db], connections: [.. edges]);
        var shuffled = Architecture(
            nodes: [db, payments, api, gateway],
            connections: [edges[2], edges[0], edges[1]]);

        var analysis = Analyze(straight);

        Assert.Equal(
            MarkdownExporter.Compose(straight, analysis, ExportedAt),
            MarkdownExporter.Compose(shuffled, analysis, ExportedAt));
    }

    [Fact]
    public void Configuration_keys_are_sorted_rather_than_left_in_document_order()
    {
        // One more collection that would make the golden files flicker: JSON preserves the order it
        // was written in, and nothing guarantees what that is.
        var one = Architecture(nodes: [Node("Api", NodeType.Api, "{\"instances\":2,\"authentication\":true}")]);
        var other = Architecture(nodes: [Node("Api", NodeType.Api, "{\"authentication\":true,\"instances\":2}")]);

        Assert.Equal(
            MarkdownExporter.Compose(one, Analyze(one), ExportedAt),
            MarkdownExporter.Compose(other, Analyze(other), ExportedAt));
    }

    // ---- Ajustes 2 y 3: the empty architecture -----------------------------

    [Fact]
    public void An_empty_architecture_produces_a_valid_document()
    {
        // The first button anybody presses. Ajustes 2 and 3 both land here.
        var empty = Architecture(name: "Nueva arquitectura");

        var document = MarkdownExporter.Compose(empty, Analyze(empty), ExportedAt);

        Assert.Contains("# Nueva arquitectura", document);
        Assert.Contains("**Sin puntuación: ninguna área es evaluable todavía.**", document);
        Assert.DoesNotContain("/ 100", document);
        Assert.DoesNotContain("## Conexiones", document);
    }

    [Fact]
    public void An_area_with_no_applicable_rule_says_so_instead_of_printing_a_zero()
    {
        // Ajuste 2. V2 D1.2: an area nobody evaluates is not a healthy area, and a 0 would say the
        // opposite.
        var architecture = Architecture(nodes: [Node("Api", NodeType.Api)]);
        var analysis = Analyze(architecture);

        var document = MarkdownExporter.Compose(architecture, analysis, ExportedAt);

        foreach (var area in analysis.Areas.Where(a => a.State == AreaState.NotApplicable))
            Assert.Contains($"| {area.Area} | sin reglas aplicables |", document);

        foreach (var area in analysis.Areas.Where(a => a.State == AreaState.NoRules))
            Assert.Contains($"| {area.Area} | sin reglas |", document);
    }

    [Fact]
    public void The_six_areas_are_emitted_in_the_declaration_order_of_the_enum()
    {
        // Already contract since V2 D2.1; the document inherits it rather than inventing an order.
        var architecture = ThreeTier();

        var document = MarkdownExporter.Compose(architecture, Analyze(architecture), ExportedAt);

        var positions = Enum.GetValues<Area>().Select(a => document.IndexOf($"| {a} |", StringComparison.Ordinal)).ToList();

        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.OrderBy(p => p), positions);
    }

    // ---- Ajuste 4: escaping ------------------------------------------------

    [Fact]
    public void A_node_named_to_break_the_document_does_not_break_it()
    {
        var awkward = Node("Orders | Payments [v2]", NodeType.Service);
        var db = Node("Db", NodeType.Database);
        var architecture = Architecture(nodes: [awkward, db], connections: [Connection(awkward, db, ProtocolType.Sql)]);

        var document = MarkdownExporter.Compose(architecture, Analyze(architecture), ExportedAt);

        // Every row of the components table has exactly the three cells its header declares, and
        // the escaped pipe of the name does not count as a separator.
        var row = document.Split('\n').Single(l => l.StartsWith("| Orders \\| Payments [v2] | Service", StringComparison.Ordinal));

        Assert.Equal(4, Regex.Matches(row, @"(?<!\\)\|").Count);
    }

    // ---- What must never reach the document --------------------------------

    [Fact]
    public void The_document_contains_no_guid()
    {
        // FindingElement travels with a Guid, and resolving those to names is the composer's job.
        // A golden file would not catch this on its own if the fixture's Guids happen to be stable.
        var architecture = ThreeTier();

        var document = MarkdownExporter.Compose(architecture, Analyze(architecture), ExportedAt);

        Assert.DoesNotMatch(new Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"), document);
    }

    [Fact]
    public void The_document_names_the_elements_of_every_finding()
    {
        var architecture = ThreeTier();
        var analysis = Analyze(architecture);
        var document = MarkdownExporter.Compose(architecture, analysis, ExportedAt);

        Assert.NotEmpty(analysis.Findings);

        foreach (var finding in analysis.Findings.Where(f => f.Elements.Count > 0))
            Assert.Contains($"### {Mark(finding.Severity)} {finding.Code}", document);
    }

    private static string Mark(Severity severity) => severity switch
    {
        Severity.Critical => "🔴",
        Severity.Warning => "🟡",
        _ => "🟢"
    };

    private static Domain.Entities.Architecture ThreeTier()
    {
        var gateway = Node("Edge", NodeType.Gateway, "{\"authentication\":true}");
        var api = Node("Orders API", NodeType.Api, "{\"authentication\":true,\"instances\":2}");
        var payments = Node("Payments", NodeType.Service);
        var db = Node("Orders DB", NodeType.Database);

        return Architecture(
            name: "Orders Platform",
            nodes: [gateway, api, payments, db],
            connections:
            [
                Connection(gateway, api),
                Connection(api, payments, configuration: "{\"retries\":3}"),
                Connection(api, db, ProtocolType.Sql, configuration: "{\"timeoutSeconds\":5}")
            ]);
    }
}
