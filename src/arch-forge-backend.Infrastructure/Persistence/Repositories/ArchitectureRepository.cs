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

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var entry = ex.Entries.Single();
            var databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken);
            var currentVersion = databaseValues is null
                ? 0
                : (int)databaseValues[nameof(Architecture.Version)]!;

            throw new ArchitectureConcurrencyConflictException(currentVersion);
        }
    }
}
