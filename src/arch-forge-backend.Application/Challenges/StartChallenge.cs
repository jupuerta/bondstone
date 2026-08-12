using arch_forge_backend.Application.Architectures;
using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Interfaces;

namespace arch_forge_backend.Application.Challenges;

public class StartChallengeCommand
{
    public string Slug { get; set; } = string.Empty;
}

/// <summary>
/// V4 D2.3 and D4.3. Instantiates the challenge's starter architecture and hands back an ordinary
/// architecture with an ordinary share token.
///
/// <strong>Ordinary is the point.</strong> An attempt is not a special kind of entity (D2.2): it
/// saves, analyses, simulates and shares exactly like anything else, and the only thing that links
/// it to the challenge is the URL. That is what keeps V4 from needing a column, a table or a
/// migration for any of this.
/// </summary>
public class StartChallengeCommandHandler
{
    private readonly IArchitectureRepository _repository;
    private readonly ChallengeCatalog _catalog;

    public StartChallengeCommandHandler(IArchitectureRepository repository, ChallengeCatalog catalog)
    {
        _repository = repository;
        _catalog = catalog;
    }

    public async Task<ArchitectureDto?> Handle(StartChallengeCommand command, CancellationToken cancellationToken = default)
    {
        var definition = _catalog.Find(command.Slug);
        var starter = _catalog.StarterOf(command.Slug);

        if (definition is null || starter is null)
            return null;

        var architecture = starter.Instantiate(definition.Title);

        await _repository.AddAsync(architecture, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return architecture.ToDto();
    }
}
