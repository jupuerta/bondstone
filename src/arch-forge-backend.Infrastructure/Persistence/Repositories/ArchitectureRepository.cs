using arch_forge_backend.Application.Common.Exceptions;
using arch_forge_backend.Application.Common.Interfaces;
using arch_forge_backend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace arch_forge_backend.Infrastructure.Persistence.Repositories;

public class ArchitectureRepository : IArchitectureRepository
{
    private readonly ArchForgeDbContext _context;

    public ArchitectureRepository(ArchForgeDbContext context)
    {
        _context = context;
    }

    public Task<Architecture?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Architectures
            .Include(a => a.Nodes)
            .Include(a => a.Connections)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<Architecture?> GetByShareTokenAsync(string shareToken, CancellationToken cancellationToken = default) =>
        _context.Architectures
            .Include(a => a.Nodes)
            .Include(a => a.Connections)
            .FirstOrDefaultAsync(a => a.ShareToken == shareToken, cancellationToken);

    public Task<Architecture?> GetByEditTokenAsync(string editToken, CancellationToken cancellationToken = default) =>
        _context.Architectures
            .Include(a => a.Nodes)
            .Include(a => a.Connections)
            .FirstOrDefaultAsync(a => a.EditToken == editToken, cancellationToken);

    public async Task AddAsync(Architecture architecture, CancellationToken cancellationToken = default) =>
        await _context.Architectures.AddAsync(architecture, cancellationToken);

    public void Remove(Architecture architecture) =>
        _context.Architectures.Remove(architecture);

    public async Task ReplaceGraphAsync(
        Architecture architecture,
        IReadOnlyCollection<Node> nodes,
        IReadOnlyCollection<Connection> connections,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        _context.Connections.RemoveRange(architecture.Connections);
        _context.Nodes.RemoveRange(architecture.Nodes);
        architecture.Connections.Clear();
        architecture.Nodes.Clear();

        await SaveChangesAsync(cancellationToken);

        await _context.Nodes.AddRangeAsync(nodes, cancellationToken);
        await _context.Connections.AddRangeAsync(connections, cancellationToken);

        await SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordSnapshotAsync(ArchitectureSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var exists = await _context.ArchitectureSnapshots
            .AnyAsync(s => s.ArchitectureId == snapshot.ArchitectureId && s.Version == snapshot.Version, cancellationToken);

        if (exists)
            return;

        _context.ArchitectureSnapshots.Add(snapshot);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ArchitectureSnapshot>> GetSnapshotsAsync(
        Guid architectureId,
        CancellationToken cancellationToken = default) =>
        await _context.ArchitectureSnapshots
            .Where(s => s.ArchitectureId == architectureId)
            .OrderBy(s => s.Version)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ArchitectureConcurrencyConflictException(await ReadCurrentVersionAsync(ex, cancellationToken));
        }
    }

    private static async Task<int> ReadCurrentVersionAsync(DbUpdateConcurrencyException exception, CancellationToken cancellationToken)
    {
        var entry = exception.Entries.FirstOrDefault(e => e.Entity is Architecture);
        if (entry is null)
            return 0;

        var databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken);
        return databaseValues is null ? 0 : (int)databaseValues[nameof(Architecture.Version)]!;
    }
}
