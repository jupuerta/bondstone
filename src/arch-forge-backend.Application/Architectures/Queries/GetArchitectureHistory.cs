using arch_forge_backend.Application.Common.Interfaces;

namespace arch_forge_backend.Application.Architectures.Queries;

public class GetArchitectureHistoryQuery
{
    public Guid Id { get; set; }
}

/// <param name="Score">Null when no area was scorable at that version.</param>
public sealed record ArchitectureSnapshotDto(int Version, string ScoringVersion, int? Score, DateTime CreatedAt);

/// <summary>
/// V4 D5.4, and the debt V2 wrote as <em>"la narrativa <c>61 → 78</c> entre sesiones"</em>.
///
/// Oldest first and capped: what the panel tells is where you started and where you are, and the
/// forty saves in between are not a story. V3 D6.5 already gave the in-session version of this for
/// the simulator without persisting anything; this is the half that survives closing the tab.
/// </summary>
public class GetArchitectureHistoryQueryHandler
{
    /// <summary>Enough to find the first entry and the last few; the panel needs two numbers.</summary>
    public const int Limit = 100;

    private readonly IArchitectureRepository _repository;

    public GetArchitectureHistoryQueryHandler(IArchitectureRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<ArchitectureSnapshotDto>?> Handle(
        GetArchitectureHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var architecture = await _repository.GetByIdAsync(query.Id, cancellationToken);
        if (architecture is null)
            return null;

        var snapshots = await _repository.GetSnapshotsAsync(query.Id, Limit, cancellationToken);

        return snapshots
            .Select(s => new ArchitectureSnapshotDto(s.Version, s.ScoringVersion, s.Score, s.CreatedAt))
            .ToList();
    }
}
