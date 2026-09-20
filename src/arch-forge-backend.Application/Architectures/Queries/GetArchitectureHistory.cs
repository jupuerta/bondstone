using System.Text.Json;
using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Common.Interfaces;
using bondstone_backend.Domain.Entities;

namespace bondstone_backend.Application.Architectures.Queries;

public class GetArchitectureHistoryQuery
{
    public string ShareToken { get; set; } = string.Empty;
}

public sealed record ArchitectureSnapshotDto(
    int Version,
    string ScoringVersion,
    int? Score,
    AnalysisSummary? Summary,
    DateTime CreatedAt,
    IReadOnlyDictionary<Area, int>? Areas = null);

public class GetArchitectureHistoryQueryHandler
{
    public const int MaxPoints = 60;

    private readonly IArchitectureRepository _repository;

    public GetArchitectureHistoryQueryHandler(IArchitectureRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<ArchitectureSnapshotDto>?> Handle(
        GetArchitectureHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var architecture = await _repository.GetByShareTokenAsync(query.ShareToken, cancellationToken);
        if (architecture is null)
            return null;

        var snapshots = await _repository.GetSnapshotsAsync(architecture.Id, cancellationToken);

        return Cap(Collapse(snapshots))
            .Select(s => new ArchitectureSnapshotDto(
                s.Version,
                s.ScoringVersion,
                s.Score,
                ReadSummary(s.Summary),
                s.CreatedAt,
                SnapshotAreas.Read(s.Areas)))
            .ToList();
    }

    private static List<ArchitectureSnapshot> Collapse(IReadOnlyList<ArchitectureSnapshot> snapshots)
    {
        var kept = new List<ArchitectureSnapshot>();

        for (var index = 0; index < snapshots.Count; index++)
        {
            var current = snapshots[index];
            var previous = kept.Count > 0 ? kept[^1] : null;
            var isEdge = index == 0 || index == snapshots.Count - 1;

            if (isEdge
                || previous is null
                || current.Score != previous.Score
                || current.ScoringVersion != previous.ScoringVersion)
            {
                kept.Add(current);
            }
        }

        return kept;
    }

    private static IReadOnlyList<ArchitectureSnapshot> Cap(List<ArchitectureSnapshot> points) =>
        points.Count <= MaxPoints
            ? points
            : [points[0], .. points.Skip(points.Count - (MaxPoints - 1))];

    private static AnalysisSummary? ReadSummary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return null;

        try
        {
            return JsonSerializer.Deserialize<AnalysisSummary>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
