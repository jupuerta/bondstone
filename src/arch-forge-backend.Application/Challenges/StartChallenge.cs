using arch_forge_backend.Application.Architectures;
using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Interfaces;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace arch_forge_backend.Application.Challenges;

public class StartChallengeCommand
{
    public string Slug { get; set; } = string.Empty;
    public string? Name { get; set; }

}

// The same ceiling the Name column enforces (varchar(200)) — without this, the 201st character
// surfaced as a DbUpdateException-turned-500 instead of the 400 every other handler gives.
public class StartChallengeCommandValidator : AbstractValidator<StartChallengeCommand>
{
    public StartChallengeCommandValidator()
    {
        RuleFor(c => c.Name).MaximumLength(200);
    }
}

public class StartChallengeCommandHandler
{
    private readonly IArchitectureRepository _repository;
    private readonly ChallengeCatalog _catalog;
    private readonly IValidator<StartChallengeCommand> _validator;
    private readonly ILogger<StartChallengeCommandHandler> _logger;

    public StartChallengeCommandHandler(
        IArchitectureRepository repository,
        ChallengeCatalog catalog,
        IValidator<StartChallengeCommand> validator,
        ILogger<StartChallengeCommandHandler> logger)
    {
        _repository = repository;
        _catalog = catalog;
        _validator = validator;
        _logger = logger;
    }

    internal static string NameOr(string? name, string slug) =>
        string.IsNullOrWhiteSpace(name) ? slug : name.Trim();

    public async Task<ArchitectureCreatedDto?> Handle(StartChallengeCommand command, CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(command, cancellationToken);

        var definition = _catalog.Find(command.Slug);
        var starter = _catalog.StarterOf(command.Slug);

        if (definition is null || starter is null)
            return null;

        var architecture = starter.Instantiate(NameOr(command.Name, definition.Slug));

        await _repository.AddAsync(architecture, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Started challenge {ChallengeSlug} as architecture {ArchitectureId}.", definition.Slug, architecture.Id);

        return architecture.ToCreatedDto();
    }
}
