using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Common.Interfaces;

public interface IArchitectureRepository
{
    Task<Architecture?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Architecture?> GetByShareTokenAsync(string shareToken, CancellationToken cancellationToken = default);
    Task AddAsync(Architecture architecture, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
