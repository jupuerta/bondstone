using arch_forge_backend.Application.Analysis;
using System.Text.Json;
using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Exceptions;
using arch_forge_backend.Application.Common.Interfaces;
using arch_forge_backend.Application.Configuration;
using arch_forge_backend.Application.Configuration.Schemas;
using arch_forge_backend.Domain.Entities;
using FluentValidation;

namespace arch_forge_backend.Application.Architectures.Commands;

public class UpdateArchitectureCommand
{
    public string EditToken { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public List<NodeDto> Nodes { get; set; } = new();
    public List<ConnectionDto> Connections { get; set; } = new();
}

public class UpdateArchitectureCommandValidator : AbstractValidator<UpdateArchitectureCommand>
{
    public UpdateArchitectureCommandValidator()
    {
        RuleFor(c => c.EditToken).NotEmpty();
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

            connection.RuleFor(c => c.TargetNodeId)
                .NotEqual(c => c.SourceNodeId)
                .WithMessage("A connection cannot point at its own source node.");
        });

        RuleFor(c => c.Connections)
            .Must(HaveUniqueIds)
            .WithMessage("Connection ids must be unique within an architecture.");

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
    private static readonly JsonSerializerOptions ResilienceSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IArchitectureRepository _repository;
    private readonly NodeConfigurationRegistry _configurationRegistry;
    private readonly IValidator<UpdateArchitectureCommand> _validator;
    private readonly ArchitectureAnalyzer _analyzer;

    public UpdateArchitectureCommandHandler(
        IArchitectureRepository repository,
        NodeConfigurationRegistry configurationRegistry,
        IValidator<UpdateArchitectureCommand> validator,
        ArchitectureAnalyzer analyzer)
    {
        _repository = repository;
        _configurationRegistry = configurationRegistry;
        _validator = validator;
        _analyzer = analyzer;
    }

    private async Task RecordSnapshot(
        Architecture architecture,
        IReadOnlyCollection<Node> nodes,
        IReadOnlyCollection<Connection> connections,
        CancellationToken cancellationToken)
    {
        try
        {
            var analysis = _analyzer.Analyze(new ArchitectureGraph(nodes.ToList(), connections.ToList()));

            await _repository.RecordSnapshotAsync(new ArchitectureSnapshot
            {
                Id = Guid.NewGuid(),
                ArchitectureId = architecture.Id,
                Version = architecture.Version,
                ScoringVersion = analysis.ScoringVersion,
                Score = analysis.Score,
                Summary = JsonSerializer.Serialize(analysis.Summary),
                Areas = SnapshotAreas.Write(analysis.Areas),
                CreatedAt = architecture.UpdatedAt
            }, cancellationToken);
        }
        catch (Exception)
        {
        }
    }

    public async Task<UpdateArchitectureResult?> Handle(UpdateArchitectureCommand command, CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(command, cancellationToken);

        var architecture = await _repository.GetByEditTokenAsync(command.EditToken, cancellationToken);
        if (architecture is null)
            return null;

        if (architecture.Version != command.Version)
            throw new ArchitectureConcurrencyConflictException(architecture.Version);

        ValidateNodeConfigurations(command.Nodes);
        ValidateConnectionConfigurations(command.Connections);

        var nodes = command.Nodes.Select(n => new Node
        {
            Id = n.Id,
            ArchitectureId = architecture.Id,
            Type = n.Type,
            Name = n.Name,
            PositionX = n.Position.X,
            PositionY = n.Position.Y,
            Configuration = ToStoredConfiguration(n.Configuration)
        }).ToList();

        var connections = command.Connections.Select(c => new Connection
        {
            Id = c.Id,
            ArchitectureId = architecture.Id,
            SourceNodeId = c.SourceNodeId,
            TargetNodeId = c.TargetNodeId,
            Protocol = c.Protocol,
            Synchronous = c.Synchronous,
            Configuration = ToStoredConfiguration(c.Configuration)
        }).ToList();

        architecture.Name = command.Name;
        architecture.Version += 1;
        architecture.UpdatedAt = DateTime.UtcNow;

        await _repository.ReplaceGraphAsync(architecture, nodes, connections, cancellationToken);
        await RecordSnapshot(architecture, nodes, connections, cancellationToken);

        return new UpdateArchitectureResult { Version = architecture.Version, UpdatedAt = architecture.UpdatedAt };
    }

    private static string ToStoredConfiguration(JsonElement? configuration) =>
        configuration is { } value && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value.GetRawText()
            : Node.EmptyConfiguration;

    private static void ValidateConnectionConfigurations(List<ConnectionDto> connections)
    {
        foreach (var connection in connections)
        {
            if (connection.Configuration is not { } configuration)
                continue;

            if (configuration.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;

            if (configuration.ValueKind != JsonValueKind.Object)
                throw new InvalidConnectionConfigurationException(connection.Id, "configuration must be a JSON object.");

            ResilienceConfiguration resilience;
            try
            {
                resilience = JsonSerializer.Deserialize<ResilienceConfiguration>(
                    configuration.GetRawText(), ResilienceSerializerOptions)!;
            }
            catch (JsonException ex)
            {
                throw new InvalidConnectionConfigurationException(connection.Id, ex.Message);
            }

            if (ConfigurationRanges.Violation(resilience) is { } violation)
                throw new InvalidConnectionConfigurationException(connection.Id, violation);
        }
    }

    private void ValidateNodeConfigurations(List<NodeDto> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Configuration is not { } configuration)
                continue;

            if (configuration.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;

            if (configuration.ValueKind != JsonValueKind.Object)
                throw new InvalidNodeConfigurationException(node.Name, "configuration must be a JSON object.");

            if (!_configurationRegistry.TryGetProvider(node.Type, out var provider))
                continue;

            object parsed;
            try
            {
                parsed = provider.Deserialize(configuration);
            }
            catch (JsonException ex)
            {
                throw new InvalidNodeConfigurationException(node.Name, ex.Message);
            }

            if (ConfigurationRanges.Violation(parsed) is { } violation)
                throw new InvalidNodeConfigurationException(node.Name, violation);
        }
    }
}
