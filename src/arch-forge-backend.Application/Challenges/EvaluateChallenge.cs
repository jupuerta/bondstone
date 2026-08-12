using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Common.Interfaces;
using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Challenges;

public class EvaluateChallengeQuery
{
    public string Slug { get; set; } = string.Empty;
    public Guid ArchitectureId { get; set; }
}

/// <summary>
/// V4 D1.2 and D3.4. A query, like <c>/analyze</c> and <c>/simulate</c> and for the same reason:
/// it persists nothing. Whether a challenge is passed is a function of a saved architecture and a
/// definition in the repo, so storing it would only create something that can go stale (D1.4).
/// </summary>
public class EvaluateChallengeQueryHandler
{
    private readonly IArchitectureRepository _repository;
    private readonly ChallengeCatalog _catalog;
    private readonly ArchitectureAnalyzer _analyzer;
    private readonly SimulationEngine _engine;

    public EvaluateChallengeQueryHandler(
        IArchitectureRepository repository,
        ChallengeCatalog catalog,
        ArchitectureAnalyzer analyzer,
        SimulationEngine engine)
    {
        _repository = repository;
        _catalog = catalog;
        _analyzer = analyzer;
        _engine = engine;
    }

    public async Task<ChallengeResult?> Handle(EvaluateChallengeQuery query, CancellationToken cancellationToken = default)
    {
        var definition = _catalog.Find(query.Slug);
        if (definition is null)
            return null;

        var architecture = await _repository.GetByIdAsync(query.ArchitectureId, cancellationToken);
        if (architecture is null)
            return null;

        return Evaluate(definition, () => new ArchitectureGraph(architecture.Nodes, architecture.Connections));
    }

    /// <summary>
    /// Takes a factory rather than a graph so <strong>every engine gets its own</strong>. The
    /// analyzer publishes into <see cref="ArchitectureGraph.Facts"/> as it runs, and V3 D5.2 is
    /// explicit that a simulation must not depend on which rules happen to have run - handing the
    /// same instance to both would make that invariant a matter of trust instead of construction.
    /// The tests reuse this entry point, which is why it is not private.
    /// </summary>
    public ChallengeResult Evaluate(ChallengeDefinition definition, Func<ArchitectureGraph> graph)
    {
        var analysis = _analyzer.Analyze(graph());

        var context = new EvaluationContext(graph(), analysis, scenario => _engine.Run(graph(), scenario));

        var requirements = definition.Requirements
            .Select(r => RequirementEvaluator.Evaluate(r, context))
            .ToList();

        var optional = definition.OptionalGoals
            .Select(r => RequirementEvaluator.Evaluate(r, context))
            .ToList();

        return new ChallengeResult(
            definition.Slug,
            requirements.All(r => r.Met),
            requirements,
            optional);
    }
}
