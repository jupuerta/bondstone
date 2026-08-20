using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Export;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Enums;
using Xunit;
using static arch_forge_backend.Application.Tests.Export.ExportFixtures;

namespace arch_forge_backend.Application.Tests.Export;

/// <summary>V5 VS2. The simulation section, and the distinction 5.1 of the plan draws at the endpoint.</summary>
public class MarkdownExporterSimulationTests
{
    [Fact]
    public void Without_a_scenario_there_is_no_simulation_section()
    {
        var architecture = Simple();

        var document = MarkdownExporter.Compose(architecture, Analyze(architecture), ExportedAt);

        Assert.DoesNotContain("## Simulación", document);
        Assert.DoesNotContain("classDef", document);
    }

    [Fact]
    public void A_null_scenario_and_an_empty_one_produce_different_documents()
    {
        // The only distinction an integration test can confuse, and the reason the endpoint reads a
        // nullable body: an empty scenario is what the screen shows on entering Simulate before
        // choosing anything (V3 D2.1), and that is a section with everything green - not no section.
        var architecture = Simple();
        var analysis = Analyze(architecture);

        var without = MarkdownExporter.Compose(architecture, analysis, ExportedAt);
        var empty = Simulated(architecture, analysis, SimulationScenario.Empty);

        Assert.NotEqual(without, empty);
        Assert.DoesNotContain("## Simulación", without);
        Assert.Contains("## Simulación", empty);
    }

    [Fact]
    public void An_empty_scenario_says_so_and_paints_nothing()
    {
        var architecture = Simple();

        var document = Simulated(architecture, Analyze(architecture), SimulationScenario.Empty);

        Assert.Contains("**Sin fallos inyectados**", document);
        Assert.Contains("Ningún componente se ve afectado", document);

        // No state, no styling: a green diagram carries not one line of classDef.
        Assert.DoesNotContain("classDef", document);
    }

    [Fact]
    public void The_same_graph_exports_two_different_documents_according_to_the_mode()
    {
        // The definition of "done" for VS2.
        var architecture = Simple();
        var analysis = Analyze(architecture);
        var db = architecture.Nodes.Single(n => n.Type == NodeType.Database);

        var design = MarkdownExporter.Compose(architecture, analysis, ExportedAt);
        var simulate = Simulated(architecture, analysis, new SimulationScenario([
            new Fault(ElementType.Node, db.Id, FaultKinds.Down)
        ]));

        Assert.NotEqual(design, simulate);
        Assert.Contains("`Db` caído", simulate);
        Assert.Contains("classDef down", simulate);
    }

    [Fact]
    public void An_injected_latency_says_how_much()
    {
        var architecture = Simple();
        var db = architecture.Nodes.Single(n => n.Type == NodeType.Database);

        var document = Simulated(architecture, Analyze(architecture), new SimulationScenario([
            new Fault(ElementType.Node, db.Id, FaultKinds.Latency, 500)
        ]));

        Assert.Contains("`Db` con 500 ms de latencia añadida", document);
    }

    [Fact]
    public void The_simulated_document_contains_no_guid_either()
    {
        // The bottleneck and every CausedBy travel as Guids. Same rule as the analysis section.
        var architecture = Simple();
        var db = architecture.Nodes.Single(n => n.Type == NodeType.Database);

        var document = Simulated(architecture, Analyze(architecture), new SimulationScenario([
            new Fault(ElementType.Node, db.Id, FaultKinds.Down)
        ]));

        Assert.DoesNotMatch(
            new System.Text.RegularExpressions.Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"),
            document);
    }

    [Fact]
    public void A_fault_pointing_at_something_that_is_gone_is_announced()
    {
        // V3 D3.6: a link shared before the architecture was edited still opens, and the faults it
        // lost are dropped out loud.
        var architecture = Simple();

        var document = Simulated(architecture, Analyze(architecture), new SimulationScenario([
            new Fault(ElementType.Node, Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), FaultKinds.Down)
        ]));

        Assert.Contains("ya no están en la arquitectura", document);
    }

    private static string Simulated(Domain.Entities.Architecture architecture, AnalysisResult analysis, SimulationScenario scenario)
    {
        var graph = new ArchitectureGraph(architecture.Nodes, architecture.Connections);
        return MarkdownExporter.Compose(architecture, analysis, ExportedAt, new SimulationEngine().Run(graph, scenario), scenario);
    }

    private static Domain.Entities.Architecture Simple()
    {
        var api = Node("Api", NodeType.Api);
        var db = Node("Db", NodeType.Database);

        return Architecture(
            nodes: [api, db],
            connections: [Connection(api, db, ProtocolType.Sql, configuration: "{\"timeoutSeconds\":5}")]);
    }
}
