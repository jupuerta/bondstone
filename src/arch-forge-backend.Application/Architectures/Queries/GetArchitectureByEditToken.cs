using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Interfaces;

namespace arch_forge_backend.Application.Architectures.Queries;

public class GetArchitectureByEditTokenQuery
{
    public string EditToken { get; set; } = string.Empty;
}

public class GetArchitectureByEditTokenQueryHandler
{
    private readonly IArchitectureRepository _repository;

    public GetArchitectureByEditTokenQueryHandler(IArchitectureRepository repository)
    {
        _repository = repository;
    }

    public async Task<ArchitectureDto?> Handle(GetArchitectureByEditTokenQuery query, CancellationToken cancellationToken = default)
    {
        var architecture = await _repository.GetByEditTokenAsync(query.EditToken, cancellationToken);
        return architecture?.ToDto();
    }
}
