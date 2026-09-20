using bondstone_backend.Application.Architectures;
using bondstone_backend.Application.Architectures.DTOs;
using bondstone_backend.Application.Common.Interfaces;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace bondstone_backend.Application.Challenges;

public class OpenChallengeSolutionCommand
{
    public string Slug { get; set; } = string.Empty;
    public string? Name { get; set; }

}

// Same reason as StartChallengeCommandValidator: the column is varchar(200), and the database is
// not the place a name should be refused.
public class OpenChallengeSolutionCommandValidator : AbstractValidator<OpenChallengeSolutionCommand>
{
    public OpenChallengeSolutionCommandValidator()
    {
        RuleFor(c => c.Name).MaximumLength(200);
    }
}

public class OpenChallengeSolutionCommandHandler
{
    private readonly IArchitectureRepository _repository;
    private readonly ChallengeCatalog _catalog;
    private readonly IValidator<OpenChallengeSolutionCommand> _validator;
    private readonly ILogger<OpenChallengeSolutionCommandHandler> _logger;

    public OpenChallengeSolutionCommandHandler(
        IArchitectureRepository repository,
        ChallengeCatalog catalog,
        IValidator<OpenChallengeSolutionCommand> validator,
        ILogger<OpenChallengeSolutionCommandHandler> logger)
    {
        _repository = repository;
        _catalog = catalog;
        _validator = validator;
        _logger = logger;
    }

    public async Task<ArchitectureDto?> Handle(OpenChallengeSolutionCommand command, CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(command, cancellationToken);

        var definition = _catalog.Find(command.Slug);
        var solution = _catalog.ReferenceSolutionOf(command.Slug);

        if (definition is null || solution is null)
            return null;

        var architecture = solution.Instantiate(
            StartChallengeCommandHandler.NameOr(command.Name, $"{definition.Slug} — reference solution"));

        await _repository.AddAsync(architecture, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        // The row this leaves is the only undeletable kind — no edit token goes back to the
        // caller — so its origin is the one most worth writing down.
        _logger.LogInformation("Opened the reference solution of {ChallengeSlug} as architecture {ArchitectureId}.", definition.Slug, architecture.Id);

        return architecture.ToDto();
    }
}
