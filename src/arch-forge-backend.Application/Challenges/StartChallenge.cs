using arch_forge_backend.Application.Architectures;
using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Interfaces;

namespace arch_forge_backend.Application.Challenges;

public class StartChallengeCommand
{
    public string Slug { get; set; } = string.Empty;
    public string? Name { get; set; }

}

public class StartChallengeCommandHandler
{
    private readonly IArchitectureRepository _repository;
    private readonly ChallengeCatalog _catalog;

    public StartChallengeCommandHandler(IArchitectureRepository repository, ChallengeCatalog catalog)
    {
        _repository = repository;
        _catalog = catalog;
    }

    internal static string NameOr(string? name, string slug) =>
        string.IsNullOrWhiteSpace(name) ? slug : name.Trim();

    public async Task<ArchitectureCreatedDto?> Handle(StartChallengeCommand command, CancellationToken cancellationToken = default)
    {
        var definition = _catalog.Find(command.Slug);
        var starter = _catalog.StarterOf(command.Slug);

        if (definition is null || starter is null)
            return null;

        var architecture = starter.Instantiate(NameOr(command.Name, definition.Slug));

        await _repository.AddAsync(architecture, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return architecture.ToCreatedDto();
    }
}
