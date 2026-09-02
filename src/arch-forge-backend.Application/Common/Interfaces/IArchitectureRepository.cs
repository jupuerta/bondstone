using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Common.Interfaces;

public interface IArchitectureRepository
{
    Task<Architecture?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Architecture?> GetByShareTokenAsync(string shareToken, CancellationToken cancellationToken = default);

    Task<Architecture?> GetByEditTokenAsync(string editToken, CancellationToken cancellationToken = default);

    Task AddAsync(Architecture architecture, CancellationToken cancellationToken = default);

    void Remove(Architecture architecture);

    Task ReplaceGraphAsync(
        Architecture architecture,
        IReadOnlyCollection<Node> nodes,
        IReadOnlyCollection<Connection> connections,
        CancellationToken cancellationToken = default);

    Task RecordSnapshotAsync(ArchitectureSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArchitectureSnapshot>> GetSnapshotsAsync(Guid architectureId, CancellationToken cancellationToken = default);

    /// <summary>Newest first, tracked — the retention policy in the update handler mutates the tail in place.</summary>
    Task<IReadOnlyList<ArchitectureSnapshot>> GetLatestSnapshotsAsync(Guid architectureId, int count, CancellationToken cancellationToken = default);

    Task<int> CountSnapshotsAsync(Guid architectureId, CancellationToken cancellationToken = default);

    /// <summary>Oldest first — what the retention belt trims, always sparing the first row.</summary>
    Task<IReadOnlyList<ArchitectureSnapshot>> GetOldestSnapshotsAsync(Guid architectureId, int count, CancellationToken cancellationToken = default);

    void RemoveSnapshots(IReadOnlyCollection<ArchitectureSnapshot> snapshots);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
