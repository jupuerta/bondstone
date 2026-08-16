using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Tests.Export;

/// <summary>
/// Builders for the export tests. The date is fixed everywhere: the document is a pure function of
/// its inputs, and the timestamp is a parameter precisely so it can be (V5 D3.3).
/// </summary>
internal static class ExportFixtures
{
    public static readonly DateTime ExportedAt = new(2026, 8, 16, 10, 30, 0, DateTimeKind.Utc);

    public static Architecture Architecture(
        string name = "Test",
        string shareToken = "8fK29x",
        int version = 1,
        List<Node>? nodes = null,
        List<Connection>? connections = null) => new()
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = name,
            ShareToken = shareToken,
            Version = version,
            Nodes = nodes ?? [],
            Connections = connections ?? []
        };

    public static Node Node(string name, NodeType type = NodeType.Service, string configuration = "{}", Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Type = type,
        Name = name,
        Configuration = configuration
    };

    public static Connection Connection(
        Node source,
        Node target,
        ProtocolType protocol = ProtocolType.Http,
        bool synchronous = true,
        string configuration = "{}",
        Guid? id = null) => new()
        {
            Id = id ?? Guid.NewGuid(),
            SourceNodeId = source.Id,
            TargetNodeId = target.Id,
            Protocol = protocol,
            Synchronous = synchronous,
            Configuration = configuration
        };

    /// <summary>The fifteen real rules, in the registration order of <c>Program.cs</c>.</summary>
    public static ArchitectureAnalyzer Analyzer() => new([
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
        new NoObservabilityRule()
    ]);

    public static AnalysisResult Analyze(Architecture architecture) =>
        Analyzer().Analyze(new ArchitectureGraph(architecture.Nodes, architecture.Connections));
}
