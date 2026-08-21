using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Interfaces;

namespace arch_forge_backend.Application.Architectures.Queries;

public class GetArchitectureByEditTokenQuery
{
    public string EditToken { get; set; } = string.Empty;
}

/// <summary>
/// Loads the architecture the owner opened their own edit link to. Sibling of
/// <see cref="GetArchitectureByShareTokenQueryHandler"/> and deliberately not merged with it: the
/// two differ only in which column they match against, but a single endpoint that decided "am I
/// the owner?" from the value the caller happened to send would be a branch nobody could see from
/// outside, and the kind of thing a future change quietly breaks.
///
/// Returns the ordinary <see cref="ArchitectureDto"/>, not <see cref="ArchitectureCreatedDto"/>: the
/// caller already holds the edit token, it is sitting in their address bar, so there is nothing to
/// echo back - only <see cref="ArchitectureDto.ShareToken"/> is new information here, and it was
/// already public.
/// </summary>
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
