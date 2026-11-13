using bondstone_backend.Application.Architectures.DTOs;
using bondstone_backend.Application.Common.Interfaces;

namespace bondstone_backend.Application.Architectures.Queries;

public class GetArchitectureByShareTokenQuery
{
    public string ShareToken { get; set; } = string.Empty;
}

public class GetArchitectureByShareTokenQueryHandler
{
    private readonly IArchitectureRepository _repository;

    public GetArchitectureByShareTokenQueryHandler(IArchitectureRepository repository)
    {
        _repository = repository;
    }

    public async Task<ArchitectureDto?> Handle(GetArchitectureByShareTokenQuery query, CancellationToken cancellationToken = default)
    {
        var architecture = await _repository.GetByShareTokenAsync(query.ShareToken, cancellationToken);
        return architecture?.ToDto();
    }
}
