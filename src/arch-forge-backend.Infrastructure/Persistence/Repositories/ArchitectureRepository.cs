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

    public async Task AddAsync(Architecture architecture, CancellationToken cancellationToken = default) =>
        await _context.Architectures.AddAsync(architecture, cancellationToken);

    public async Task ReplaceGraphAsync(
        Architecture architecture,
        IReadOnlyCollection<Node> nodes,
        IReadOnlyCollection<Connection> connections,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // Delete the current graph first. It goes in the same SaveChanges as the Architecture
        // UPDATE, which is where the Version concurrency token is checked - so a stale write is
        // rejected before anything is inserted.
        _context.Connections.RemoveRange(architecture.Connections);
        _context.Nodes.RemoveRange(architecture.Nodes);
        architecture.Connections.Clear();
        architecture.Nodes.Clear();

        await SaveChangesAsync(cancellationToken);

        // Insert through the DbSet, not through architecture.Nodes/Connections: the incoming
        // entities carry client-generated keys, and change tracking would read a set key on an
        // untracked entity as Modified, issuing an UPDATE against rows that do not exist.
        await _context.Nodes.AddRangeAsync(nodes, cancellationToken);
        await _context.Connections.AddRangeAsync(connections, cancellationToken);

        await SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

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
