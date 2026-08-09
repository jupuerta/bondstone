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
        RuleFor(c => c.Version).GreaterThan(0);

        RuleForEach(c => c.Nodes).ChildRules(node =>
        {
            node.RuleFor(n => n.Id).NotEmpty().WithMessage("Each node must carry a client-generated id.");
            node.RuleFor(n => n.Name).NotEmpty().MaximumLength(200);
            node.RuleFor(n => n.Type).IsInEnum();
        });

        RuleFor(c => c.Nodes)
            .Must(HaveUniqueIds)
            .WithMessage("Node ids must be unique within an architecture.");

        RuleForEach(c => c.Connections).ChildRules(connection =>
        {
            connection.RuleFor(c => c.Id).NotEmpty().WithMessage("Each connection must carry a client-generated id.");
            connection.RuleFor(c => c.Protocol).IsInEnum();
            connection.RuleFor(c => c.TimeoutSeconds).GreaterThan(0).When(c => c.TimeoutSeconds.HasValue);
            connection.RuleFor(c => c.Retries).GreaterThanOrEqualTo(0).When(c => c.Retries.HasValue);
        });

        RuleFor(c => c.Connections)
            .Must(HaveUniqueIds)
            .WithMessage("Connection ids must be unique within an architecture.");

        // The PUT carries a full snapshot, so every edge has to resolve against the nodes in the
        // same payload - otherwise it would be persisted pointing at a node that does not exist.
        RuleFor(c => c.Connections)
            .Must((command, connections) =>
            {
                var nodeIds = command.Nodes.Select(n => n.Id).ToHashSet();
                return connections.All(c => nodeIds.Contains(c.SourceNodeId) && nodeIds.Contains(c.TargetNodeId));
            })
            .WithMessage("Every connection must reference nodes included in the same request.");
    }

    private static bool HaveUniqueIds(List<NodeDto> nodes) =>
        nodes.Select(n => n.Id).Distinct().Count() == nodes.Count;

    private static bool HaveUniqueIds(List<ConnectionDto> connections) =>
        connections.Select(c => c.Id).Distinct().Count() == connections.Count;
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

        var nodes = command.Nodes.Select(n => new Node
        {
            Id = n.Id,
            ArchitectureId = architecture.Id,
            Type = n.Type,
            Name = n.Name,
            PositionX = n.Position.X,
            PositionY = n.Position.Y,
            Configuration = n.Configuration?.GetRawText()
        }).ToList();

        var connections = command.Connections.Select(c => new Connection
        {
            Id = c.Id,
            ArchitectureId = architecture.Id,
            SourceNodeId = c.SourceNodeId,
            TargetNodeId = c.TargetNodeId,
            Protocol = c.Protocol,
            Synchronous = c.Synchronous,
            TimeoutSeconds = c.TimeoutSeconds,
            Retries = c.Retries
        }).ToList();

        architecture.Name = command.Name;
        architecture.Version += 1;
        architecture.UpdatedAt = DateTime.UtcNow;

        await _repository.ReplaceGraphAsync(architecture, nodes, connections, cancellationToken);

        return new UpdateArchitectureResult { Version = architecture.Version, UpdatedAt = architecture.UpdatedAt };
    }

    private void ValidateNodeConfigurations(List<NodeDto> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Configuration is not { } configuration)
                continue;

            if (configuration.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;

            // Guards the jsonb column: anything that is not an object would either fail at the
            // database or make the typed schema meaningless.
            if (configuration.ValueKind != JsonValueKind.Object)
                throw new InvalidNodeConfigurationException(node.Name, "configuration must be a JSON object.");

            if (!_configurationRegistry.TryGetProvider(node.Type, out var provider))
                continue;

            try
            {
                provider.Deserialize(configuration);
            }
            catch (JsonException ex)
            {
                throw new InvalidNodeConfigurationException(node.Name, ex.Message);
            }
        }
    }
}
