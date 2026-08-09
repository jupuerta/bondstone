using System.Text.Json;
using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Exceptions;
using arch_forge_backend.Application.Common.Interfaces;
using arch_forge_backend.Application.Configuration;
using arch_forge_backend.Domain.Entities;
using FluentValidation;

namespace arch_forge_backend.Application.Architectures.Commands;

public class UpdateArchitectureCommand
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public List<NodeDto> Nodes { get; set; } = new();
    public List<ConnectionDto> Connections { get; set; } = new();
}

public class UpdateArchitectureCommandValidator : AbstractValidator<UpdateArchitectureCommand>
{
    public UpdateArchitectureCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
    }
}

public class UpdateArchitectureCommandHandler
{
    private readonly IArchitectureRepository _repository;
    private readonly NodeConfigurationRegistry _configurationRegistry;
    private readonly IValidator<UpdateArchitectureCommand> _validator;

    public UpdateArchitectureCommandHandler(
        IArchitectureRepository repository,
        NodeConfigurationRegistry configurationRegistry,
        IValidator<UpdateArchitectureCommand> validator)
    {
        _repository = repository;
        _configurationRegistry = configurationRegistry;
        _validator = validator;
    }

    public async Task<UpdateArchitectureResult?> Handle(UpdateArchitectureCommand command, CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(command, cancellationToken);

        var architecture = await _repository.GetByIdAsync(command.Id, cancellationToken);
        if (architecture is null)
            return null;

        if (architecture.Version != command.Version)
            throw new ArchitectureConcurrencyConflictException(architecture.Version);

        ValidateNodeConfigurations(command.Nodes);

        architecture.Name = command.Name;

        architecture.Nodes = command.Nodes.Select(n => new Node
        {
            Id = n.Id == Guid.Empty ? Guid.NewGuid() : n.Id,
            ArchitectureId = architecture.Id,
            Type = n.Type,
            Name = n.Name,
            PositionX = n.PositionX,
            PositionY = n.PositionY,
            Configuration = n.Configuration
        }).ToList();

        architecture.Connections = command.Connections.Select(c => new Connection
        {
            Id = c.Id == Guid.Empty ? Guid.NewGuid() : c.Id,
            ArchitectureId = architecture.Id,
            SourceNodeId = c.SourceNodeId,
            TargetNodeId = c.TargetNodeId,
            Protocol = c.Protocol,
            Synchronous = c.Synchronous,
            TimeoutSeconds = c.TimeoutSeconds,
            Retries = c.Retries
        }).ToList();

        architecture.Version += 1;
        architecture.UpdatedAt = DateTime.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new UpdateArchitectureResult { Version = architecture.Version, UpdatedAt = architecture.UpdatedAt };
    }

    private void ValidateNodeConfigurations(List<NodeDto> nodes)
    {
        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Configuration))
                continue;

            if (!_configurationRegistry.TryGetProvider(node.Type, out var provider))
                continue;

            try
            {
                using var document = JsonDocument.Parse(node.Configuration);
                provider.Deserialize(document.RootElement);
            }
            catch (JsonException ex)
            {
                throw new InvalidNodeConfigurationException(node.Name, ex.Message);
            }
        }
    }
}
