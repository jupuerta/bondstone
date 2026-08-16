using arch_forge_backend.Application.Common.Interfaces;
using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Tests.TestDoubles;

public class FakeArchitectureRepository : IArchitectureRepository
{
    private readonly Dictionary<Guid, Architecture> _store = new();

    /// <summary>Puts an architecture in the store without going through AddAsync.</summary>
    public void Seed(Architecture architecture) => _store[architecture.Id] = architecture;

    public Task<Architecture?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.GetValueOrDefault(id));

    public Task<Architecture?> GetByShareTokenAsync(string shareToken, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.Values.FirstOrDefault(a => a.ShareToken == shareToken));

    public Task AddAsync(Architecture architecture, CancellationToken cancellationToken = default)
    {
        _store[architecture.Id] = architecture;
        return Task.CompletedTask;
    }

    // The real one leans on the database cascade; here dropping the entry drops the graph with it,
    // because the nodes and connections hang off the Architecture object.
    public void Remove(Architecture architecture) => _store.Remove(architecture.Id);

    public Task ReplaceGraphAsync(
        Architecture architecture,
        IReadOnlyCollection<Node> nodes,
        IReadOnlyCollection<Connection> connections,
        CancellationToken cancellationToken = default)
    {
        architecture.Nodes = nodes.ToList();
        architecture.Connections = connections.ToList();
        _store[architecture.Id] = architecture;
        return Task.CompletedTask;
    }

    /// <summary>V4 D5.4: recorded in memory so a handler test can assert what was written.</summary>
    public List<ArchitectureSnapshot> Snapshots { get; } = [];

    public Task RecordSnapshotAsync(ArchitectureSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (!Snapshots.Any(s => s.ArchitectureId == snapshot.ArchitectureId && s.Version == snapshot.Version))
            Snapshots.Add(snapshot);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ArchitectureSnapshot>> GetSnapshotsAsync(Guid architectureId, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ArchitectureSnapshot>>(
            Snapshots.Where(s => s.ArchitectureId == architectureId).OrderBy(s => s.Version).Take(limit).ToList());

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
