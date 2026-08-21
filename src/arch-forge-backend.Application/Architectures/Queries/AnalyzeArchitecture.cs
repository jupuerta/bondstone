using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Common.Interfaces;

namespace arch_forge_backend.Application.Architectures.Queries;

public class AnalyzeArchitectureQuery
{
    public string ShareToken { get; set; } = string.Empty;
}

public class AnalyzeArchitectureQueryHandler
{
    private readonly IArchitectureRepository _repository;
    private readonly ArchitectureAnalyzer _analyzer;

    public AnalyzeArchitectureQueryHandler(IArchitectureRepository repository, ArchitectureAnalyzer analyzer)
    {
        _repository = repository;
        _analyzer = analyzer;
    }

    public async Task<AnalysisResult?> Handle(AnalyzeArchitectureQuery query, CancellationToken cancellationToken = default)
    {
        var architecture = await _repository.GetByShareTokenAsync(query.ShareToken, cancellationToken);
        if (architecture is null)
            return null;

        var graph = new ArchitectureGraph(architecture.Nodes, architecture.Connections);
        return _analyzer.Analyze(graph);
    }
}
