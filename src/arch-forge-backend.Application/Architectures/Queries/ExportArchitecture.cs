using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Common.Interfaces;
using arch_forge_backend.Application.Export;
using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Architectures.Queries;

/// <summary>
/// V5 D3.1. The fourth instance of the shape <c>/analyze</c>, <c>/simulate</c> and <c>/evaluate</c>
/// already have: it reads the <em>saved</em> architecture, computes a pure function over it, and
/// <strong>persists nothing</strong>. It introduces no new concept into the API.
///
/// It lives in the backend rather than the client - which already holds every contract in memory -
/// for the reason the plan técnico §3.1 argues: what the screen shows is already the saved version
/// (the analyzer reads from the database and <c>useLiveAnalysis</c> fires after the autosave), so
/// the client's only real advantage disappears, and the golden files of D3.3 get to run the real
/// analyzer in-process against the four fixtures instead of maintaining captured copies of it.
/// </summary>
public class ExportArchitectureQuery
{
    public Guid Id { get; set; }

    /// <summary>
    /// Injected rather than read from the clock so the document is a pure function of its inputs -
    /// the precondition of D3.3. The handler supplies <c>UtcNow</c>; a test supplies a fixed date.
    /// </summary>
    public DateTime? ExportedAtUtc { get; set; }

    /// <summary>
    /// V5 VS2. Null and empty are <strong>not the same</strong>: null produces a document with no
    /// simulation section, and an empty scenario produces one with everything green - which is what
    /// the screen shows on entering Simulate before choosing anything (V3 D2.1).
    /// </summary>
    public SimulationScenario? Scenario { get; set; }
}

public class ExportArchitectureQueryHandler
{
    private readonly IArchitectureRepository _repository;
    private readonly ArchitectureAnalyzer _analyzer;
    private readonly SimulationEngine _engine;

    public ExportArchitectureQueryHandler(
        IArchitectureRepository repository,
        ArchitectureAnalyzer analyzer,
        SimulationEngine engine)
    {
        _repository = repository;
        _analyzer = analyzer;
        _engine = engine;
    }

    public async Task<string?> Handle(ExportArchitectureQuery query, CancellationToken cancellationToken = default)
    {
        var architecture = await _repository.GetByIdAsync(query.Id, cancellationToken);
        if (architecture is null)
            return null;

        var analysis = _analyzer.Analyze(new ArchitectureGraph(architecture.Nodes, architecture.Connections));

        // A graph of its own for the simulation, with no rule run over it: `Facts` is the channel
        // between rules, and a simulation reading from it would change its answer depending on
        // which rules happen to be registered (V3 D5.2).
        var simulation = query.Scenario is null
            ? null
            : _engine.Run(new ArchitectureGraph(architecture.Nodes, architecture.Connections), query.Scenario);

        return MarkdownExporter.Compose(
            architecture,
            analysis,
            query.ExportedAtUtc ?? DateTime.UtcNow,
            simulation,
            query.Scenario);
    }
}
