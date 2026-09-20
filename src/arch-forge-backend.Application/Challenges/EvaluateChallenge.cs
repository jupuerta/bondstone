using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Common.Interfaces;
using bondstone_backend.Application.Simulation;

namespace bondstone_backend.Application.Challenges;

public class EvaluateChallengeQuery
{
    public string Slug { get; set; } = string.Empty;
    public string ShareToken { get; set; } = string.Empty;
}

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

        var architecture = await _repository.GetByShareTokenAsync(query.ShareToken, cancellationToken);
        if (architecture is null)
            return null;

        return Evaluate(definition, () => new ArchitectureGraph(architecture.Nodes, architecture.Connections));
    }

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
