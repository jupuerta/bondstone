using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Common.Interfaces;
using arch_forge_backend.Application.Export;

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
}

public class ExportArchitectureQueryHandler
{
    private readonly IArchitectureRepository _repository;
    private readonly ArchitectureAnalyzer _analyzer;

    public ExportArchitectureQueryHandler(IArchitectureRepository repository, ArchitectureAnalyzer analyzer)
    {
        _repository = repository;
        _analyzer = analyzer;
    }

    public async Task<string?> Handle(ExportArchitectureQuery query, CancellationToken cancellationToken = default)
    {
        var architecture = await _repository.GetByIdAsync(query.Id, cancellationToken);
        if (architecture is null)
            return null;

        var graph = new ArchitectureGraph(architecture.Nodes, architecture.Connections);
        var analysis = _analyzer.Analyze(graph);

        return MarkdownExporter.Compose(
            architecture,
            analysis,
            query.ExportedAtUtc ?? DateTime.UtcNow);
    }
}
