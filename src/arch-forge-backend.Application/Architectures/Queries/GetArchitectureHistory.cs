using System.Text.Json;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Common.Interfaces;
using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Architectures.Queries;

public class GetArchitectureHistoryQuery
{
    public string ShareToken { get; set; } = string.Empty;
}

/// <param name="Score">Null when no area was scorable at that version.</param>
/// <param name="Summary">
/// What the analysis found at that version. Null where the snapshot never carried counts, which is
/// not the same claim as a version that found nothing.
/// </param>
public sealed record ArchitectureSnapshotDto(
    int Version,
    string ScoringVersion,
    int? Score,
    AnalysisSummary? Summary,
    DateTime CreatedAt);

/// <summary>
/// V4 D5.4, and the debt V2 wrote as <em>"la narrativa <c>61 → 78</c> entre sesiones"</em>.
///
/// One point per <em>change</em> of score, never one per save. The autosave writes a snapshot a
/// second after every edit, so most versions carry the number the one before them already carried:
/// moving a node or renaming it writes a version and does not move the score. Collapsing those runs
/// is what turns a hundred saves into the handful of moments where the architecture actually got
/// better or worse — the shape the panel draws — and it leaves every point a real event, which
/// matters because each point is also the hit target for reading that version's detail.
/// </summary>
public class GetArchitectureHistoryQueryHandler
{
    /// <summary>
    /// A ceiling on what is returned, not on what is read. The collapse has to see every version to
    /// know which ones are changes, and capping the read is what the previous
    /// <c>OrderBy(Version).Take(100)</c> got wrong in both directions: it dropped the newest
    /// versions, so the line stopped at v100 and the current score was never in it, while reversing
    /// the order would instead have dropped the oldest — the entry <c>61 →</c> is measured from.
    /// After collapsing this almost never binds: it takes sixty score changes to reach it.
    /// </summary>
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
                s.CreatedAt))
            .ToList();
    }

    /// <summary>
    /// Keeps a snapshot when it is the first, when it is the last, or when it says something the
    /// last kept one did not.
    ///
    /// The last is kept even when it repeats: it is the version on screen right now, so it is where
    /// the panel puts the current-score marker, and a run of saves that did not move the number is
    /// itself worth seeing on an "Improve" screen. A change of scoring version counts as different
    /// at the same number — two scoring versions are not comparable (V2 D1.5), so the moment the
    /// model changed is exactly a moment the reader needs.
    /// </summary>
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

    /// <summary>
    /// If sixty changes were not enough, keep the first — the anchor the narrative is measured
    /// from — and the most recent ones, which is where the work is.
    /// </summary>
    private static IReadOnlyList<ArchitectureSnapshot> Cap(List<ArchitectureSnapshot> points) =>
        points.Count <= MaxPoints
            ? points
            : [points[0], .. points.Skip(points.Count - (MaxPoints - 1))];

    /// <summary>
    /// The counts as <c>UpdateArchitecture</c> wrote them. Null rather than zeroes when there is
    /// nothing to read: the column defaults to <c>'{}'</c> and rows written before it carried
    /// anything still hold that, and "0 critical, 0 warnings, 0 passed" is a claim about the
    /// architecture that those rows are not making.
    /// </summary>
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
            // A snapshot with unreadable counts still has a version and a score, and those are what
            // the line is drawn from. Losing the detail beats losing the point.
            return null;
        }
    }
}
