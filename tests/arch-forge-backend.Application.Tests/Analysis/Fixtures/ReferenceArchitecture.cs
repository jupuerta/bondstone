using System.Text.Json;
using System.Text.Json.Serialization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Tests.Analysis.Fixtures;

/// <summary>
/// Loads one of the reference architectures and runs the real twenty rules over it (D5.5). With
/// twenty rules, precedences and shared facts, the interaction is where the risk lives: changing a
/// HIGH_COUPLING threshold moves the score of architectures that have nothing to do with coupling,
/// and no unit test notices.
/// </summary>
public static class ReferenceArchitecture
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AnalysisResult Analyze(string fixtureName) => Analyze(Graph(fixtureName));

    /// <summary>
    /// The twenty rules over a graph the caller built - which is how the shuffling test feeds the
    /// same fixture in a different order (V6 D1.1).
    /// </summary>
    public static AnalysisResult Analyze(ArchitectureGraph graph)
    {
        // Registered in the same order as Program.cs, and deliberately not in execution order.
        var analyzer = new ArchitectureAnalyzer([
            new SpofRule(),
            new CycleRule(),
            new RetryAmplificationRule(),
            new SyncDependencyChainRule(),
            new MissingResilienceRule(),
            new HighCouplingRule(),
            new MissingCacheRule(),
            new OrphanNodeRule(),
            new QueueNoDlqRule(),
            new TimeoutTooShortRule(),
            new UndersizedComponentRule(),
            new NoRateLimitRule(),
            new ExposedDatabaseRule(),
            new UnauthenticatedEntryRule(),
            new NoObservabilityRule(),
            new ExternalNoFallbackRule(),
            new ProtocolMismatchRule(),
            new QueueNoConsumerRule(),
            new CacheAsStoreRule(),
            new SharedDatabaseRule(),
            new ImplausibleDependencyRule()
        ]);

        return analyzer.Analyze(graph);
    }

    /// <summary>
    /// The same four fixtures with no rule run over them, which is what the simulator gets (V3
    /// D5.2): <c>Facts</c> is the producer-to-consumer channel between rules, and a simulation that
    /// read from it would change its answer depending on which rules happen to be registered.
    /// </summary>
    public static ArchitectureGraph Graph(string fixtureName)
    {
        var (nodes, connections) = Load(fixtureName);
        return new ArchitectureGraph(nodes, connections);
    }

    public static (List<Node> Nodes, List<Connection> Connections) Load(string fixtureName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Analysis", "Fixtures", "Architectures", $"{fixtureName}.json");
        var fixture = JsonSerializer.Deserialize<Fixture>(File.ReadAllText(path), Options)
                      ?? throw new InvalidOperationException($"Fixture '{fixtureName}' could not be read.");

        var nodes = fixture.Nodes.Select(n => new Node
        {
            Id = n.Id,
            Type = n.Type,
            Name = n.Name,
            Configuration = Raw(n.Configuration)
        }).ToList();

        var connections = fixture.Connections.Select(c => new Connection
        {
            Id = c.Id,
            SourceNodeId = c.SourceNodeId,
            TargetNodeId = c.TargetNodeId,
            Protocol = c.Protocol,
            Synchronous = c.Synchronous,
            Configuration = Raw(c.Configuration)
        }).ToList();

        return (nodes, connections);
    }

    public static Guid NodeId(this ArchitectureGraph graph, string name) =>
        graph.Nodes.Single(n => n.Name == name).Id;

    public static Guid EdgeId(this ArchitectureGraph graph, string source, string target) =>
        graph.Connections.Single(c =>
            graph.NodesById[c.SourceNodeId].Name == source &&
            graph.NodesById[c.TargetNodeId].Name == target).Id;

    public static int? ScoreOf(this AnalysisResult result, Area area) =>
        result.Areas.Single(a => a.Area == area).Score;

    public static AreaState StateOf(this AnalysisResult result, Area area) =>
        result.Areas.Single(a => a.Area == area).State;

    public static IEnumerable<string> CodesOf(this AnalysisResult result, Severity severity) =>
        result.Findings.Where(f => f.Severity == severity).Select(f => f.Code);

    private static string Raw(JsonElement? configuration) =>
        configuration is { } value && value.ValueKind == JsonValueKind.Object
            ? value.GetRawText()
            : "{}";

    private sealed record Fixture(string Name, List<FixtureNode> Nodes, List<FixtureConnection> Connections);

    private sealed record FixtureNode(Guid Id, NodeType Type, string Name, JsonElement? Configuration);

    private sealed record FixtureConnection(
        Guid Id,
        Guid SourceNodeId,
        Guid TargetNodeId,
        ProtocolType Protocol,
        bool Synchronous,
        JsonElement? Configuration);
}
