using System.Globalization;
using System.Text;
using System.Text.Json;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Export;

/// <summary>
/// V5 D1.1. The architecture, its diagram and its analysis as one Markdown document that serves a
/// person and a model at the same time - because a good prompt is a document with no ambiguity, and
/// a good architecture report is the same thing minus the request at the end.
///
/// <strong>Static and pure</strong>, like <c>CallCost</c> and <c>LatencyModel</c>: same inputs, same
/// text, no state and nothing injected. The timestamp arrives <em>as a parameter</em> and is never
/// read from <c>DateTime.UtcNow</c> - otherwise the golden files of D3.3 would be impossible,
/// because the header would change on every run.
///
/// <strong>V7 D1.2. The language is an input too</strong>, and for the same reason: this is the one
/// place in the product that still renders prose on the server, because a Markdown file cannot
/// resolve a key on its own. Everything the analyzer and the simulator hand it is a
/// <see cref="LocalizedText"/>; the <see cref="Localizer"/> is what turns the whole document -
/// findings, reasons and its own headings alike - into one language rather than two.
/// </summary>
public static class MarkdownExporter
{
    public static string Compose(
        Architecture architecture,
        AnalysisResult analysis,
        DateTime exportedAtUtc,
        Localizer text,
        SimulationResult? simulation = null,
        SimulationScenario? scenario = null)
    {
        // Plan técnico §6.2, ajuste 1. Everything emitted as a list is ordered explicitly: the
        // repository does not guarantee an order, so without this the same graph could produce two
        // different documents and the golden files would flicker without anybody touching anything.
        var nodes = architecture.Nodes
            .OrderBy(n => n.Type)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .ThenBy(n => n.Id)
            .ToList();

        var namesById = nodes.ToDictionary(n => n.Id, n => n.Name);

        var connections = architecture.Connections
            .OrderBy(c => NameOf(namesById, c.SourceNodeId, text), StringComparer.Ordinal)
            .ThenBy(c => NameOf(namesById, c.TargetNodeId, text), StringComparer.Ordinal)
            .ThenBy(c => c.Id)
            .ToList();

        var builder = new StringBuilder();

        var health = simulation?.States
            .Where(s => s.Type == ElementType.Node)
            .ToDictionary(s => s.Id, s => s.State);

        Header(builder, architecture, analysis, exportedAtUtc, text);
        Diagram(builder, nodes, connections, health, text);
        Components(builder, nodes, text);
        Connections(builder, connections, namesById, text);
        Analysis(builder, analysis, namesById, connections, text);
        Simulation(builder, simulation, scenario, namesById, connections, text);
        Prompt(builder, text);

        return builder.ToString().TrimEnd() + "\n";
    }

    // ---- Header -----------------------------------------------------------

    private static void Header(
        StringBuilder builder,
        Architecture architecture,
        AnalysisResult analysis,
        DateTime exportedAtUtc,
        Localizer text)
    {
        builder.AppendLine($"# {architecture.Name}");
        builder.AppendLine();
        builder.AppendLine("| | |");
        builder.AppendLine("|---|---|");
        builder.AppendLine(
            $"| {text.Of("export.header.architecture")} " +
            $"| `{MarkdownEscape.TableCell(architecture.ShareToken)}` · {text.Of("export.header.version", ("version", architecture.Version))} |");
        builder.AppendLine($"| {text.Of("export.header.exported")} | {exportedAtUtc:yyyy-MM-dd} |");
        builder.AppendLine($"| {text.Of("export.header.engine")} | `scoringVersion: {MarkdownEscape.TableCell(analysis.ScoringVersion)}` |");
        builder.AppendLine($"| {text.Of("export.header.format")} | `exportVersion: {ExportVersion.Current}` |");
        builder.AppendLine();
    }

    // ---- Diagram ----------------------------------------------------------

    private static void Diagram(
        StringBuilder builder,
        IReadOnlyList<Node> nodes,
        IReadOnlyList<Connection> connections,
        IReadOnlyDictionary<Guid, HealthState>? health,
        Localizer text)
    {
        builder.AppendLine($"## {text.Of("export.section.diagram")}");
        builder.AppendLine();

        if (nodes.Count == 0)
        {
            builder.AppendLine(text.Of("export.diagram.empty"));
            builder.AppendLine();
            return;
        }

        builder.AppendLine(MermaidEmitter.Emit(nodes, connections, health));
        builder.AppendLine();
    }

    // ---- Components -------------------------------------------------------

    private static void Components(StringBuilder builder, IReadOnlyList<Node> nodes, Localizer text)
    {
        builder.AppendLine($"## {text.Of("export.section.components")}");
        builder.AppendLine();

        if (nodes.Count == 0)
        {
            builder.AppendLine(text.Of("export.components.none"));
            builder.AppendLine();
            return;
        }

        builder.AppendLine(
            $"| {text.Of("export.column.name")} " +
            $"| {text.Of("export.column.type")} " +
            $"| {text.Of("export.column.configuration")} |");
        builder.AppendLine("|---|---|---|");

        foreach (var node in nodes)
        {
            builder.AppendLine(
                $"| {MarkdownEscape.TableCell(node.Name)} " +
                $"| {node.Type} " +
                $"| {Configuration(node.Configuration)} |");
        }

        builder.AppendLine();
    }

    // ---- Connections ------------------------------------------------------

    private static void Connections(
        StringBuilder builder,
        IReadOnlyList<Connection> connections,
        IReadOnlyDictionary<Guid, string> namesById,
        Localizer text)
    {
        // §6.1: the section disappears when there is none, rather than emitting a header with
        // nothing under it. That is the state of a freshly created architecture, which together
        // with a null score is when somebody presses the button for the first time.
        if (connections.Count == 0)
            return;

        builder.AppendLine($"## {text.Of("export.section.connections")}");
        builder.AppendLine();
        builder.AppendLine(
            $"| {text.Of("export.column.source")} " +
            $"| {text.Of("export.column.target")} " +
            $"| {text.Of("export.column.protocol")} " +
            $"| {text.Of("export.column.synchronous")} " +
            $"| {text.Of("export.column.timeout")} " +
            $"| {text.Of("export.column.retries")} " +
            $"| {text.Of("export.column.circuitBreaker")} " +
            $"| {text.Of("export.column.fallback")} |");
        builder.AppendLine("|---|---|---|---|---|---|---|---|");

        var yes = text.Of("export.yes");
        var no = text.Of("export.no");

        foreach (var connection in connections)
        {
            var resilience = Resilience(connection.Configuration);

            builder.AppendLine(
                $"| {MarkdownEscape.TableCell(NameOf(namesById, connection.SourceNodeId, text))} " +
                $"| {MarkdownEscape.TableCell(NameOf(namesById, connection.TargetNodeId, text))} " +
                $"| {connection.Protocol} " +
                $"| {(connection.Synchronous ? yes : no)} " +
                $"| {(resilience.TimeoutSeconds is { } t ? text.Of("export.seconds", ("seconds", t)) : Dash)} " +
                $"| {(resilience.Retries is { } r ? r.ToString(CultureInfo.InvariantCulture) : Dash)} " +
                $"| {(resilience.CircuitBreaker ? yes : no)} " +
                $"| {(resilience.Fallback ? yes : no)} |");
        }

        builder.AppendLine();
    }

    // ---- Analysis ---------------------------------------------------------

    private static void Analysis(
        StringBuilder builder,
        AnalysisResult analysis,
        IReadOnlyDictionary<Guid, string> namesById,
        IReadOnlyList<Connection> connections,
        Localizer text)
    {
        builder.AppendLine($"## {text.Of("export.section.analysis")}");
        builder.AppendLine();

        // Ajuste 3. Score is int? and is null when no area is scorable - "an empty architecture is
        // not a 100" (V2 D1.2). It is said in words rather than printed as a figure.
        if (analysis.Score is { } score)
        {
            // The three counts are nested keys, not three calls to a Count() helper that took a
            // singular and a plural: "1 crítico" and "2 críticos" is a rule of Spanish, and the
            // next language picks its own form from its own entry (V7 D1.2).
            builder.AppendLine(text.Of("export.analysis.score",
                ("score", score),
                ("criticals", LocalizedText.Of("export.analysis.criticals", ("count", analysis.Summary.Critical))),
                ("warnings", LocalizedText.Of("export.analysis.warnings", ("count", analysis.Summary.Warning))),
                ("passed", LocalizedText.Of("export.analysis.passed", ("count", analysis.Summary.Passed)))));
        }
        else
        {
            builder.AppendLine(text.Of("export.analysis.noScore"));
        }

        builder.AppendLine();
        builder.AppendLine($"| {text.Of("export.column.area")} | {text.Of("export.column.score")} |");
        builder.AppendLine("|---|---|");

        // The declaration order of Area is contract (V2 D2.1), so `areas` is emitted in it.
        foreach (var area in analysis.Areas)
            builder.AppendLine($"| {area.Area} | {AreaScoreText(area, text)} |");

        builder.AppendLine();

        if (analysis.Findings.Count == 0)
        {
            builder.AppendLine(text.Of("export.analysis.nothing"));
            builder.AppendLine();
            return;
        }

        var connectionsById = connections.ToDictionary(c => c.Id);

        foreach (var finding in analysis.Findings)
        {
            builder.AppendLine($"### {SeverityMark(finding.Severity)} {finding.Code} — {finding.Area}");
            builder.AppendLine();
            builder.AppendLine($"**{text.Of(finding.Message)}**");

            var explanation = text.Of(finding.Explanation);
            if (!string.IsNullOrWhiteSpace(explanation))
            {
                builder.AppendLine();
                builder.AppendLine(explanation);
            }

            if (finding.Suggestion is { } suggestion)
            {
                builder.AppendLine();
                builder.AppendLine($"{text.Of("export.analysis.suggestion")} {text.Of(suggestion)}");
            }

            var elements = Elements(finding, namesById, connectionsById, text);
            if (elements.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine($"{text.Of("export.analysis.elements")} {elements}");
            }

            builder.AppendLine();
        }
    }

    // ---- Prompt -----------------------------------------------------------

    /// <summary>
    /// V5 D2.5. <strong>The only lever the product keeps over the quality of the review</strong>,
    /// which is the acknowledged weak point of this direction: once the document leaves, what a
    /// model answers is out of reach. A well-written request lowers the bar to zero - copy, paste,
    /// send - and points the model at what the document already carries instead of letting it
    /// improvise.
    ///
    /// It goes last, under a rule and with its own heading, so whoever takes the document to a pull
    /// request deletes from the rule onwards and still has a report.
    ///
    /// <strong>It names no rule and no count of rules.</strong> D2.4 established that findings
    /// explain themselves, and a number here would age exactly the way V4 aged the analyzer when it
    /// went from seven rules to fifteen.
    /// </summary>
    private static void Prompt(StringBuilder builder, Localizer text)
    {
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine($"## {text.Of("export.section.prompt")}");
        builder.AppendLine();
        builder.AppendLine(PromptText.Value);
    }

    private static readonly Lazy<string> PromptText = new(() =>
    {
        const string resource = "arch_forge_backend.Application.Export.Prompt.md";

        using var stream = typeof(MarkdownExporter).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The embedded resource '{resource}' is missing.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    });

    // ---- Simulation -------------------------------------------------------

    /// <summary>
    /// V5 VS2. Emitted only when the screen had a simulation - D2.1: <strong>the export reflects the
    /// mode, it does not ask</strong>.
    ///
    /// An <em>empty</em> scenario is not the same as no scenario: it produces this section with
    /// everything green, which is what the screen shows on entering Simulate before choosing
    /// anything (V3 D2.1). That distinction is the one 5.1 of the plan draws at the endpoint.
    /// </summary>
    private static void Simulation(
        StringBuilder builder,
        SimulationResult? simulation,
        SimulationScenario? scenario,
        IReadOnlyDictionary<Guid, string> namesById,
        IReadOnlyList<Connection> connections,
        Localizer text)
    {
        if (simulation is null)
            return;

        var connectionsById = connections.ToDictionary(c => c.Id);
        var faults = scenario?.Canonical().Faults ?? [];

        builder.AppendLine($"## {text.Of("export.section.simulation")}");
        builder.AppendLine();

        if (faults.Count == 0)
        {
            builder.AppendLine(text.Of("export.simulation.noFaults"));
        }
        else
        {
            builder.AppendLine(text.Of("export.simulation.scenario"));
            builder.AppendLine();

            foreach (var fault in faults)
                builder.AppendLine($"- {FaultText(fault, namesById, connectionsById, text)}");
        }

        builder.AppendLine();

        // A link shared before the architecture was edited still opens, and the faults it lost are
        // announced rather than dropped in silence (V3 D3.6). The document says so too.
        if (simulation.DiscardedFaults.Count > 0)
        {
            builder.AppendLine($"> {text.Of("export.simulation.discarded", ("count", simulation.DiscardedFaults.Count))}");
            builder.AppendLine();
        }

        Metrics(builder, simulation.Metrics, namesById, connectionsById, text);

        var affected = simulation.States.Where(s => s.State != HealthState.Healthy).ToList();

        if (affected.Count == 0)
        {
            builder.AppendLine(text.Of("export.simulation.unaffected"));
            builder.AppendLine();
            return;
        }

        builder.AppendLine($"### {text.Of("export.simulation.perComponent")}");
        builder.AppendLine();

        // States arrive ordered by (PropagationStep, Type, Id), so the list already reads as the
        // order of propagation (V3 D5.6). It is not reordered: that order is the explanation.
        foreach (var state in affected)
        {
            var name = state.Type == ElementType.Node
                ? NameOf(namesById, state.Id, text)
                : connectionsById.TryGetValue(state.Id, out var edge)
                    ? $"{NameOf(namesById, edge.SourceNodeId, text)} → {NameOf(namesById, edge.TargetNodeId, text)}"
                    : text.Of("export.unknown");

            builder.AppendLine($"**{StateMark(state.State)} `{name}`** — {text.Of(state.Reason)}");

            // The most valuable pixel of V3: the learning does not happen in the red, it happens
            // where somebody decided it would stop breaking.
            if (!string.IsNullOrWhiteSpace(state.Mechanism))
                builder.AppendLine($"  · {text.Of("export.simulation.containedBy")} `{state.Mechanism}`");

            builder.AppendLine();
        }

        var healthy = simulation.States.Count - affected.Count;
        if (healthy > 0)
        {
            builder.AppendLine(text.Of("export.simulation.rest", ("count", healthy)));
            builder.AppendLine();
        }
    }

    private static void Metrics(
        StringBuilder builder,
        SimulationMetrics metrics,
        IReadOnlyDictionary<Guid, string> namesById,
        IReadOnlyDictionary<Guid, Connection> connectionsById,
        Localizer text)
    {
        builder.AppendLine($"| {text.Of("export.column.metric")} | {text.Of("export.column.value")} |");
        builder.AppendLine("|---|---|");

        Row("componentsDown", text.Of("export.metric.outOf", ("value", metrics.ComponentsDown), ("total", metrics.ComponentsTotal)));
        Row("componentsDegraded", text.Of("export.metric.outOf", ("value", metrics.ComponentsDegraded), ("total", metrics.ComponentsTotal)));
        Row("blastRadius", text.Of("export.metric.percent", ("value", metrics.BlastRadius)));
        Row("entryPointsAffected", text.Of("export.metric.outOf", ("value", metrics.EntryPointsAffected), ("total", metrics.EntryPointsTotal)));
        Row("nominalLatency", text.Of("export.metric.milliseconds", ("value", metrics.NominalLatencyMs)));

        // Null rather than 0: no entry point left standing means there is no path to measure, and a
        // 0 would read as "instant" (V3 plan técnico §2).
        Row("faultedLatency", metrics.FaultedLatencyMs is { } faulted
            ? text.Of("export.metric.milliseconds", ("value", faulted))
            : text.Of("export.metric.noRoute"));

        Row("requestsAffected", text.Of("export.metric.atMost", ("value", metrics.RequestsAffected)));

        // The bound travels with its culprit so the document can point at one component instead of
        // tinting eight (V4 DT.7).
        if (metrics.Bottleneck is { } bottleneck)
            Row("bottleneck", $"`{ElementName(bottleneck, namesById, connectionsById, text)}`");

        Row("requestAmplification", text.Of("export.metric.times", ("value", metrics.RequestAmplification)));
        Row("loadAmplification", text.Of("export.metric.times", ("value", metrics.LoadAmplification)));
        Row("backlog", text.Of("export.metric.rps", ("value", metrics.BacklogRps)));
        builder.AppendLine();

        void Row(string metric, string value) =>
            builder.AppendLine($"| {text.Of($"export.metric.{metric}")} | {value} |");
    }

    private static string FaultText(
        Fault fault,
        IReadOnlyDictionary<Guid, string> namesById,
        IReadOnlyDictionary<Guid, Connection> connectionsById,
        Localizer text)
    {
        var name = ElementName(new FindingElement(fault.Target, fault.Id), namesById, connectionsById, text);

        return fault.Kind switch
        {
            FaultKinds.Down => text.Of("export.fault.down", ("name", name)),
            FaultKinds.Degraded => text.Of("export.fault.degraded", ("name", name)),
            FaultKinds.Latency => text.Of("export.fault.latency", ("name", name), ("ms", fault.Value ?? 0)),
            _ => text.Of("export.fault.other", ("name", name), ("kind", fault.Kind))
        };
    }

    private static string ElementName(
        FindingElement element,
        IReadOnlyDictionary<Guid, string> namesById,
        IReadOnlyDictionary<Guid, Connection> connectionsById,
        Localizer text)
    {
        if (element.Type == ElementType.Node)
            return NameOf(namesById, element.Id, text);

        return connectionsById.TryGetValue(element.Id, out var connection)
            ? $"{NameOf(namesById, connection.SourceNodeId, text)} → {NameOf(namesById, connection.TargetNodeId, text)}"
            : text.Of("export.unknown");
    }

    private static string StateMark(HealthState state) => state switch
    {
        HealthState.Down => "🔴",
        HealthState.Degraded => "🟡",
        _ => "🟢"
    };

    /// <summary>
    /// Ajuste 2. <c>AreaScore.Score</c> is nullable and <c>AreaState</c> has three values. Printing
    /// a <c>0</c> or leaving the cell empty for the last two <strong>lies about the analysis</strong>,
    /// and V2 D1.2 wrote it down: an area nobody evaluates is not a healthy area.
    ///
    /// <c>NoRules</c> should not occur since V4 - all six areas score - and is emitted anyway rather
    /// than treated as impossible: it is a state of the contract, and a document that breaks or lies
    /// in front of a valid state is worse than one that writes it.
    /// </summary>
    private static string AreaScoreText(AreaScore area, Localizer text) => area.State switch
    {
        AreaState.Scored => area.Score?.ToString(CultureInfo.InvariantCulture) ?? Dash,
        AreaState.NotApplicable => text.Of("export.area.notApplicable"),
        AreaState.NoRules => text.Of("export.area.noRules"),
        _ => Dash
    };

    /// <summary>
    /// The em dash for an absent value. Not a catalogue entry: it is punctuation, and a key for it
    /// would be a translator being asked to decide something that is not language.
    /// </summary>
    private const string Dash = "—";

    private static string SeverityMark(Severity severity) => severity switch
    {
        Severity.Critical => "🔴",
        Severity.Warning => "🟡",
        _ => "🟢"
    };

    /// <summary>
    /// A finding travels with <c>FindingElement(ElementType, Guid)</c>, and resolving those Guids to
    /// names is the composer's job: <strong>no Guid may reach the document</strong>. A reader - human
    /// or model - cannot do anything with one.
    /// </summary>
    private static string Elements(
        RuleFinding finding,
        IReadOnlyDictionary<Guid, string> namesById,
        IReadOnlyDictionary<Guid, Connection> connectionsById,
        Localizer text)
    {
        var parts = new List<string>();

        foreach (var element in finding.Elements)
        {
            switch (element.Type)
            {
                case ElementType.Node when namesById.TryGetValue(element.Id, out var name):
                    parts.Add($"`{name}`");
                    break;

                case ElementType.Connection when connectionsById.TryGetValue(element.Id, out var connection):
                    parts.Add($"`{NameOf(namesById, connection.SourceNodeId, text)} → {NameOf(namesById, connection.TargetNodeId, text)}`");
                    break;
            }
        }

        return string.Join(", ", parts);
    }

    // ---- Shared -----------------------------------------------------------

    private static string NameOf(IReadOnlyDictionary<Guid, string> namesById, Guid id, Localizer text) =>
        namesById.TryGetValue(id, out var name) ? name : text.Of("export.unknown");

    /// <summary>
    /// The node's configuration, read generically rather than through
    /// <c>NodeConfigurationRegistry</c>: the exporter has no business knowing the ten schemas, and a
    /// new node type must not need a change here to be exportable.
    ///
    /// <strong>Keys are sorted.</strong> JSON preserves document order, which is one more collection
    /// that would make the golden files flicker (ajuste 1).
    /// </summary>
    private static string Configuration(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
            return "—";

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(configuration);
        }
        catch (JsonException)
        {
            return "—";
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return "—";

            var entries = document.RootElement
                .EnumerateObject()
                // A null value and an absent key mean the same thing to the registry, so the
                // document must not invent a setting out of one: `readReplicas: —` reads as a
                // configured value, which is exactly what it is not.
                .Where(p => p.Value.ValueKind != JsonValueKind.Null)
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => $"`{MarkdownEscape.TableCell(p.Name)}: {MarkdownEscape.TableCell(Scalar(p.Value))}`")
                .ToList();

            return entries.Count == 0 ? "—" : string.Join(", ", entries);
        }
    }

    private static string Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => "sí",
        JsonValueKind.False => "no",
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.GetRawText(),
        _ => value.GetRawText()
    };

    private static ResilienceView Resilience(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
            return new ResilienceView(null, null, false, false);

        try
        {
            using var document = JsonDocument.Parse(configuration);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return new ResilienceView(null, null, false, false);

            return new ResilienceView(
                Number(document.RootElement, "timeoutSeconds"),
                Number(document.RootElement, "retries"),
                Boolean(document.RootElement, "circuitBreaker"),
                Boolean(document.RootElement, "fallback"));
        }
        catch (JsonException)
        {
            return new ResilienceView(null, null, false, false);
        }
    }

    private static int? Number(JsonElement root, string name) =>
        TryGet(root, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number)
            ? number
            : null;

    private static bool Boolean(JsonElement root, string name) =>
        TryGet(root, name) is { ValueKind: JsonValueKind.True };

    /// <summary>Case-insensitive, matching how <c>ArchitectureGraph</c> reads the same object.</summary>
    private static JsonElement? TryGet(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }

    private sealed record ResilienceView(int? TimeoutSeconds, int? Retries, bool CircuitBreaker, bool Fallback);
}
