using bondstone_backend.Application.Common.Exceptions;
using bondstone_backend.Application.Common.Interfaces;
using bondstone_backend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace bondstone_backend.Infrastructure.Persistence.Repositories;

public class ArchitectureRepository : IArchitectureRepository
{
    private readonly BondstoneDbContext _context;

    public ArchitectureRepository(BondstoneDbContext context)
    {
        _context = context;
    }

    // The three reads below all pull two sibling collections off one row. In a single SQL statement
    // that is a cartesian product: every node repeated once per connection. At the validator's
    // ceilings — 150 nodes and 400 connections — one architecture is 60 000 rows on the wire, each
    // carrying its own 4 KB configuration, to answer a request whose real payload is 550 rows.
    //
    // `AsSplitQuery` asks for one statement per collection instead. The usual caveat about split
    // queries is that they see different snapshots of a moving table; here the row is loaded to be
    // read whole or replaced whole, and `ReplaceGraphAsync` does its swap inside a transaction, so
    // there is no window where half of a graph is the old one.
    public Task<Architecture?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Architectures
            .Include(a => a.Nodes)
            .Include(a => a.Connections)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<Architecture?> GetByShareTokenAsync(string shareToken, CancellationToken cancellationToken = default) =>
        _context.Architectures
            .Include(a => a.Nodes)
            .Include(a => a.Connections)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.ShareToken == shareToken, cancellationToken);

    public Task<Architecture?> GetByEditTokenAsync(string editToken, CancellationToken cancellationToken = default) =>
        _context.Architectures
            .Include(a => a.Nodes)
            .Include(a => a.Connections)
            .AsSplitQuery()
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

    // Tracked on purpose: the caller moves the tail of a flat run forward by mutating the row.
    public async Task<IReadOnlyList<ArchitectureSnapshot>> GetLatestSnapshotsAsync(
        Guid architectureId,
        int count,
        CancellationToken cancellationToken = default) =>
        await _context.ArchitectureSnapshots
            .Where(s => s.ArchitectureId == architectureId)
            .OrderByDescending(s => s.Version)
            .Take(count)
            .ToListAsync(cancellationToken);

    public Task<int> CountSnapshotsAsync(Guid architectureId, CancellationToken cancellationToken = default) =>
        _context.ArchitectureSnapshots.CountAsync(s => s.ArchitectureId == architectureId, cancellationToken);

    public async Task<IReadOnlyList<ArchitectureSnapshot>> GetOldestSnapshotsAsync(
        Guid architectureId,
        int count,
        CancellationToken cancellationToken = default) =>
        await _context.ArchitectureSnapshots
            .Where(s => s.ArchitectureId == architectureId)
            .OrderBy(s => s.Version)
            .Take(count)
            .ToListAsync(cancellationToken);

    public void RemoveSnapshots(IReadOnlyCollection<ArchitectureSnapshot> snapshots) =>
        _context.ArchitectureSnapshots.RemoveRange(snapshots);

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
