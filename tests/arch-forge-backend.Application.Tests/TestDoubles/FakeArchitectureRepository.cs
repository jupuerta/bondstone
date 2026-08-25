using arch_forge_backend.Application.Common.Interfaces;
using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Tests.TestDoubles;

public class FakeArchitectureRepository : IArchitectureRepository
{
    private readonly Dictionary<Guid, Architecture> _store = new();

    public void Seed(Architecture architecture) => _store[architecture.Id] = architecture;

    public Task<Architecture?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.GetValueOrDefault(id));

    public Task<Architecture?> GetByShareTokenAsync(string shareToken, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.Values.FirstOrDefault(a => a.ShareToken == shareToken));

    public Task<Architecture?> GetByEditTokenAsync(string editToken, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.Values.FirstOrDefault(a => a.EditToken == editToken));

    public Task AddAsync(Architecture architecture, CancellationToken cancellationToken = default)
    {
        _store[architecture.Id] = architecture;
        return Task.CompletedTask;
    }

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

    public List<ArchitectureSnapshot> Snapshots { get; } = [];

    public Task RecordSnapshotAsync(ArchitectureSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (!Snapshots.Any(s => s.ArchitectureId == snapshot.ArchitectureId && s.Version == snapshot.Version))
            Snapshots.Add(snapshot);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ArchitectureSnapshot>> GetSnapshotsAsync(Guid architectureId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ArchitectureSnapshot>>(
            Snapshots.Where(s => s.ArchitectureId == architectureId).OrderBy(s => s.Version).ToList());

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
