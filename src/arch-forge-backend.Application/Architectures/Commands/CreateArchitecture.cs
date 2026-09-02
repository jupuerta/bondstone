using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Interfaces;
using arch_forge_backend.Domain.Entities;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace arch_forge_backend.Application.Architectures.Commands;

public class CreateArchitectureCommand
{
    public string Name { get; set; } = string.Empty;
}

public class CreateArchitectureCommandValidator : AbstractValidator<CreateArchitectureCommand>
{
    public CreateArchitectureCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
    }
}

public class CreateArchitectureCommandHandler
{
    private readonly IArchitectureRepository _repository;
    private readonly IValidator<CreateArchitectureCommand> _validator;
    private readonly ILogger<CreateArchitectureCommandHandler> _logger;

    public CreateArchitectureCommandHandler(
        IArchitectureRepository repository,
        IValidator<CreateArchitectureCommand> validator,
        ILogger<CreateArchitectureCommandHandler> logger)
    {
        _repository = repository;
        _validator = validator;
        _logger = logger;
    }

    public async Task<ArchitectureCreatedDto> Handle(CreateArchitectureCommand command, CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(command, cancellationToken);

        var now = DateTime.UtcNow;
        var architecture = new Architecture
        {
            Id = Guid.NewGuid(),
            Name = command.Name,
            ShareToken = ArchitectureTokens.NewShareToken(),
            EditToken = ArchitectureTokens.NewEditToken(),
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _repository.AddAsync(architecture, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        // Minting is the one thing an anonymous client does that costs a row forever, so each row
        // says where it came from. Ids only — tokens are the keys to the row and stay out of logs.
        _logger.LogInformation("Minted architecture {ArchitectureId}.", architecture.Id);

        return architecture.ToCreatedDto();
    }
}
