using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Common.Interfaces;

public interface IArchitectureRepository
{
    Task<Architecture?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Architecture?> GetByShareTokenAsync(string shareToken, CancellationToken cancellationToken = default);
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

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
