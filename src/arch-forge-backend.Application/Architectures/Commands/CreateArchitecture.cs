using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Interfaces;
using arch_forge_backend.Domain.Entities;
using FluentValidation;

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

    public CreateArchitectureCommandHandler(IArchitectureRepository repository, IValidator<CreateArchitectureCommand> validator)
    {
        _repository = repository;
        _validator = validator;
    }

    public async Task<ArchitectureCreatedDto> Handle(CreateArchitectureCommand command, CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(command, cancellationToken);

        var now = DateTime.UtcNow;
        var architecture = new Architecture
        {
            Id = Guid.NewGuid(),
            Name = command.Name,
            ShareToken = Guid.NewGuid().ToString("N")[..10],
            EditToken = Guid.NewGuid().ToString("N"),
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _repository.AddAsync(architecture, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return architecture.ToCreatedDto();
    }
}
