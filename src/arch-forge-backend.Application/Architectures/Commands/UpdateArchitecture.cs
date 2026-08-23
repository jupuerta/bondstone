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
    /// <summary>The write handle. Resolved through it and nothing else - see <see cref="Domain.Entities.Architecture.EditToken"/>.</summary>
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

            // V2 D3.4: a self-loop represents no real architecture and would falsify every
            // traversal, so it is structural corruption rather than something to have an opinion
            // about. Cycles of length >= 2, by contrast, are analyzed, not rejected.
            connection.RuleFor(c => c.TargetNodeId)
                .NotEqual(c => c.SourceNodeId)
                .WithMessage("A connection cannot point at its own source node.");
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

    /// <summary>
    /// V4 D5.4. On save and not on analysis: the live analysis fires on a debounce while you type,
    /// so recording it there would fill the table with half-finished edits. <c>Version</c> already
    /// identifies the graph without ambiguity and only moves when something reached the database.
    ///
    /// It never takes a save down with it. The architecture is already persisted by this point, and
    /// a history entry is worth strictly less than the edit it describes.
    /// </summary>
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
                CreatedAt = architecture.UpdatedAt
            }, cancellationToken);
        }
        catch (Exception)
        {
            // Deliberately swallowed, and the only place in the codebase that does this.
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

    // The column is jsonb not null (Technical_Design_V1 §5.1): a node the client sends without
    // configuration is stored as an empty object rather than NULL.
    private static string ToStoredConfiguration(JsonElement? configuration) =>
        configuration is { } value && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value.GetRawText()
            : Node.EmptyConfiguration;

    // An edge has a single schema regardless of protocol (V2 D3.1), so it is deserialized
    // directly instead of going through the node registry. Validation happens on save, as it
    // already does for nodes.
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

            // V14 B14: the guards this method carried since V2 moved to the table the nodes now
            // share, and the timeout gained the ceiling it never had.
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

            // Guards the jsonb column: anything that is not an object would either fail at the
            // database or make the typed schema meaningless.
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

            // V14 B14. Deserializing was the whole check until V14, so a node could be saved with
            // instances: -3 and requestsPerSecond: -5000 and score a hundred. Same guards the edge
            // has had since V2, same 400 (ConfigurationRanges).
            if (ConfigurationRanges.Violation(parsed) is { } violation)
                throw new InvalidNodeConfigurationException(node.Name, violation);
        }
    }
}
