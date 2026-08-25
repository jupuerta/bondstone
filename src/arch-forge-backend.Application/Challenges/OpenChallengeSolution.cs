using arch_forge_backend.Application.Architectures;
using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Interfaces;

namespace arch_forge_backend.Application.Challenges;

public class OpenChallengeSolutionCommand
{
    public string Slug { get; set; } = string.Empty;
    public string? Name { get; set; }

}

public class OpenChallengeSolutionCommandHandler
{
    private readonly IArchitectureRepository _repository;
    private readonly ChallengeCatalog _catalog;

    public OpenChallengeSolutionCommandHandler(IArchitectureRepository repository, ChallengeCatalog catalog)
    {
        _repository = repository;
        _catalog = catalog;
    }

    public async Task<ArchitectureDto?> Handle(OpenChallengeSolutionCommand command, CancellationToken cancellationToken = default)
    {
        var definition = _catalog.Find(command.Slug);
        var solution = _catalog.ReferenceSolutionOf(command.Slug);

        if (definition is null || solution is null)
            return null;

        var architecture = solution.Instantiate(
            StartChallengeCommandHandler.NameOr(command.Name, $"{definition.Slug} — reference solution"));

        await _repository.AddAsync(architecture, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return architecture.ToDto();
    }
}
