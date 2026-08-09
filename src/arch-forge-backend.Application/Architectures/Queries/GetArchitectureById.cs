using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Interfaces;

namespace arch_forge_backend.Application.Architectures.Queries;

public class GetArchitectureByIdQuery
{
    public string ShareToken { get; set; } = string.Empty;
}

public class GetArchitectureByIdQueryHandler
{
    private readonly IArchitectureRepository _repository;

    public GetArchitectureByIdQueryHandler(IArchitectureRepository repository)
    {
        _repository = repository;
    }

    public async Task<ArchitectureDto?> Handle(GetArchitectureByIdQuery query, CancellationToken cancellationToken = default)
    {
        var architecture = await _repository.GetByShareTokenAsync(query.ShareToken, cancellationToken);
        return architecture?.ToDto();
    }
}
