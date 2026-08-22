using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Common.Interfaces;

public interface IArchitectureRepository
{
    /// <summary>
    /// By primary key. Nothing HTTP-reachable resolves a client-supplied value this way any more -
    /// the two token lookups below are what every controller action uses - so this exists for
    /// server-internal reads that already hold the entity's own id (tests included).
    /// </summary>
    Task<Architecture?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The read handle. Everything that does not write resolves through this.</summary>
    Task<Architecture?> GetByShareTokenAsync(string shareToken, CancellationToken cancellationToken = default);

    /// <summary>The write handle. <c>PUT</c> and <c>DELETE</c> resolve through this and nothing else.</summary>
    Task<Architecture?> GetByEditTokenAsync(string editToken, CancellationToken cancellationToken = default);

    Task AddAsync(Architecture architecture, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the architecture for deletion. Its nodes and connections go with it by cascade
    /// (ArchitectureConfiguration in Infrastructure declares it), so there is no graph to tear down
    /// by hand. Pairs with <see cref="SaveChangesAsync"/>, the same way <see cref="AddAsync"/> does.
    /// </summary>
    void Remove(Architecture architecture);

    /// <summary>
    /// Persists the pending changes on <paramref name="architecture"/> and replaces its whole set of
    /// nodes and connections with the given snapshot, in a single transaction
    /// (Technical_Design_V1 §3.6). Throws <see cref="Common.Exceptions.ArchitectureConcurrencyConflictException"/>
    /// if the architecture was modified by another session in the meantime.
    /// </summary>
    Task ReplaceGraphAsync(
        Architecture architecture,
        IReadOnlyCollection<Node> nodes,
        IReadOnlyCollection<Connection> connections,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// V4 D5.4. Records what the architecture scored at this version, ignoring the write if that
    /// version was already recorded - the autosave can retry, and a snapshot is a fact about a
    /// version rather than about an attempt to save it.
    /// </summary>
    Task RecordSnapshotAsync(ArchitectureSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// The whole history, oldest first. Deliberately uncapped: the history query collapses it to one
    /// point per score change, and it cannot tell what a change is without seeing every version.
    /// Bounded in practice by one architecture's own edit history, and the (ArchitectureId, Version)
    /// index already covers the ordering.
    /// </summary>
    Task<IReadOnlyList<ArchitectureSnapshot>> GetSnapshotsAsync(Guid architectureId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
