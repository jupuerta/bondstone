using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Architectures;
using bondstone_backend.Application.Architectures.DTOs;

namespace bondstone_backend.Application.Challenges;

public class PreviewChallengeSolutionQuery
{
    public string Slug { get; set; } = string.Empty;
}

public sealed record ChallengeSolutionPreview(
    string Name,
    IReadOnlyList<NodeDto> Nodes,
    IReadOnlyList<ConnectionDto> Connections,
    AnalysisResult Analysis);

/// <summary>
/// The reference solution as something to read, not as something to keep. Its sibling
/// <see cref="OpenChallengeSolutionCommandHandler"/> mints a real architecture, which is what the
/// share link needs and what nothing can ever delete; this one answers the same question — what does
/// the author's answer look like, and what does it score — without writing a row, so opening the
/// comparison stops costing one.
/// </summary>
public class PreviewChallengeSolutionQueryHandler
{
    private readonly ChallengeCatalog _catalog;
    private readonly ArchitectureAnalyzer _analyzer;

    public PreviewChallengeSolutionQueryHandler(ChallengeCatalog catalog, ArchitectureAnalyzer analyzer)
    {
        _catalog = catalog;
        _analyzer = analyzer;
    }

    public ChallengeSolutionPreview? Handle(PreviewChallengeSolutionQuery query)
    {
        var definition = _catalog.Find(query.Slug);
        var solution = _catalog.ReferenceSolutionOf(query.Slug);

        if (definition is null || solution is null)
            return null;

        var architecture = solution.Instantiate($"{definition.Slug} — reference solution");
        var dto = architecture.ToDto();

        return new ChallengeSolutionPreview(
            dto.Name,
            dto.Nodes,
            dto.Connections,
            _analyzer.Analyze(new ArchitectureGraph(architecture.Nodes, architecture.Connections)));
    }
}
