using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Challenges;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Tests.Challenges;

/// <summary>
/// Runs the real evaluator over a blueprint, with the real seven rules and the real engine behind
/// it. Nothing is stubbed: the point of V4 D3.1 is that a requirement is a comparison over two
/// contracts that already work, so a test that faked those two would be testing nothing.
/// </summary>
public static class ChallengeHarness
{
    public static readonly ChallengeCatalog Catalog = new();

    public static ChallengeResult Evaluate(string slug, ArchitectureBlueprint blueprint)
    {
        var definition = Catalog.Find(slug)
                         ?? throw new InvalidOperationException($"There is no challenge '{slug}'.");

        return Evaluate(definition, blueprint);
    }

    public static ChallengeResult Evaluate(ChallengeDefinition definition, ArchitectureBlueprint blueprint)
    {
        // Instantiated once and shared by every graph the evaluation builds. Instantiating per call
        // would mint fresh ids each time, and a fault resolved against one instance would point at
        // nothing in the next - which is exactly how this harness found the discarded-fault hole in
        // RequirementEvaluator.UnderScenario.
        var architecture = blueprint.Instantiate("test");
        return Handler().Evaluate(definition, () => new ArchitectureGraph(architecture.Nodes, architecture.Connections));
    }

    public static ChallengeResult Evaluate(ChallengeDefinition definition, Func<ArchitectureGraph> graph) =>
        Handler().Evaluate(definition, graph);

    public static ArchitectureGraph Graph(ArchitectureBlueprint blueprint)
    {
        var architecture = blueprint.Instantiate("test");
        return new ArchitectureGraph(architecture.Nodes, architecture.Connections);
    }

    /// <summary>A context over a graph with an analysis the caller controls - what the D1.4 test needs.</summary>
    public static EvaluationContext Context(ArchitectureBlueprint blueprint, AnalysisResult analysis)
    {
        var architecture = blueprint.Instantiate("test");
        return new EvaluationContext(
            new ArchitectureGraph(architecture.Nodes, architecture.Connections),
            analysis,
            scenario => new SimulationEngine().Run(new ArchitectureGraph(architecture.Nodes, architecture.Connections), scenario));
    }

    public static EvaluationContext Context(ArchitectureBlueprint blueprint) =>
        Context(blueprint, Analyze(blueprint));

    public static AnalysisResult Analyze(ArchitectureBlueprint blueprint) => Analyzer().Analyze(Graph(blueprint));

    private static EvaluateChallengeQueryHandler Handler() =>
        new(new NullRepository(), Catalog, Analyzer(), new SimulationEngine());

    // Registered in the same order as Program.cs, and deliberately not in execution order.
    private static ArchitectureAnalyzer Analyzer() => new([
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

    /// <summary>The handler's repository is only touched by <c>Handle</c>; these tests call <c>Evaluate</c>.</summary>
    private sealed class NullRepository : Application.Common.Interfaces.IArchitectureRepository
    {
        public Task<Architecture?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Architecture?>(null);

        public Task<Architecture?> GetByShareTokenAsync(string shareToken, CancellationToken cancellationToken = default) =>
            Task.FromResult<Architecture?>(null);

        public Task<Architecture?> GetByEditTokenAsync(string editToken, CancellationToken cancellationToken = default) =>
            Task.FromResult<Architecture?>(null);

        public Task AddAsync(Architecture architecture, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Remove(Architecture architecture) { }

        public Task ReplaceGraphAsync(
            Architecture architecture,
            IReadOnlyCollection<Node> nodes,
            IReadOnlyCollection<Connection> connections,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RecordSnapshotAsync(ArchitectureSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<ArchitectureSnapshot>> GetSnapshotsAsync(Guid architectureId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ArchitectureSnapshot>>([]);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
