using arch_forge_backend.Application.Analysis;
using System.Text.Json;
using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Exceptions;
using arch_forge_backend.Application.Common.Interfaces;
using arch_forge_backend.Application.Configuration;
using arch_forge_backend.Application.Configuration.Schemas;
using arch_forge_backend.Domain.Entities;
using FluentValidation;
using Microsoft.Extensions.Logging;

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
    // Ceilings on what one architecture may cost, because this is the only door a graph enters
    // through and everything downstream trusts its size. The analyzer is quadratic-ish, the
    // fragility map runs one simulation per node, and configurations are stored verbatim as
    // jsonb — so an unbounded PUT converts into unbounded CPU on every later share-token read
    // and unbounded disk on every fork. The numbers are ~10x the largest architecture the
    // product has ever drawn (catalog solutions top out around 20 nodes; a schema-full
    // configuration is a few hundred bytes), so no honest editor session ever meets them.
    public const int MaxNodes = 150;
    public const int MaxConnections = 400;
    public const int MaxConfigurationBytes = 4096;

    public UpdateArchitectureCommandValidator()
    {
        RuleFor(c => c.EditToken).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Version).GreaterThan(0);

        RuleFor(c => c.Nodes)
            .Must(nodes => nodes.Count <= MaxNodes)
            .WithMessage($"An architecture cannot hold more than {MaxNodes} nodes.");

        RuleFor(c => c.Connections)
            .Must(connections => connections.Count <= MaxConnections)
            .WithMessage($"An architecture cannot hold more than {MaxConnections} connections.");

        RuleForEach(c => c.Nodes).ChildRules(node =>
        {
            node.RuleFor(n => n.Id).NotEmpty().WithMessage("Each node must carry a client-generated id.");
            node.RuleFor(n => n.Name).NotEmpty().MaximumLength(200);
            node.RuleFor(n => n.Type).IsInEnum();

            node.RuleFor(n => n.Configuration)
                .Must(FitsConfigurationBudget)
                .WithMessage($"A node configuration cannot exceed {MaxConfigurationBytes} bytes of JSON.");
        });

        RuleFor(c => c.Nodes)
            .Must(HaveUniqueIds)
            .WithMessage("Node ids must be unique within an architecture.");

        RuleForEach(c => c.Connections).ChildRules(connection =>
        {
            connection.RuleFor(c => c.Id).NotEmpty().WithMessage("Each connection must carry a client-generated id.");

            connection.RuleFor(c => c.TargetNodeId)
                .NotEqual(c => c.SourceNodeId)
                .WithMessage("A connection cannot point at its own source node.");

            connection.RuleFor(c => c.Configuration)
                .Must(FitsConfigurationBudget)
                .WithMessage($"A connection configuration cannot exceed {MaxConfigurationBytes} bytes of JSON.");
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

    // Bytes of what would actually be stored (the handler persists GetRawText() verbatim), so the
    // budget measures the jsonb row and not some serialization that never happens.
    private static bool FitsConfigurationBudget(JsonElement? configuration) =>
        configuration is not { } value
        || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
        || System.Text.Encoding.UTF8.GetByteCount(value.GetRawText()) <= MaxConfigurationBytes;
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
    private readonly ILogger<UpdateArchitectureCommandHandler> _logger;

    public UpdateArchitectureCommandHandler(
        IArchitectureRepository repository,
        NodeConfigurationRegistry configurationRegistry,
        IValidator<UpdateArchitectureCommand> validator,
        ArchitectureAnalyzer analyzer,
        ILogger<UpdateArchitectureCommandHandler> logger)
    {
        _repository = repository;
        _configurationRegistry = configurationRegistry;
        _validator = validator;
        _analyzer = analyzer;
        _logger = logger;
    }

    // The belt for a client that changes the score on every save — a flat run costs two rows no
    // matter how long it lasts (see RecordSnapshot), but nothing else stops an alternating one
    // from minting a row per second forever. When the count spills over, the oldest rows after
    // the first go: the first is where the story starts, the recent ones are what the chart
    // shows, and the read side caps at 60 points anyway.
    public const int MaxSnapshotsPerArchitecture = 500;

    private async Task RecordSnapshot(
        Architecture architecture,
        IReadOnlyCollection<Node> nodes,
        IReadOnlyCollection<Connection> connections,
        CancellationToken cancellationToken)
    {
        try
        {
            var analysis = _analyzer.Analyze(new ArchitectureGraph(nodes.ToList(), connections.ToList()));

            // Write-side twin of GetArchitectureHistory's Collapse, so pruning changes what is
            // stored and never what is served: a flat run keeps its first point (when the score
            // got here) and a moving tail (where the editor is now). The autosave saves once a
            // second while someone drags boxes around; without this, every one of those seconds
            // was a row nobody would ever read back.
            var latest = await _repository.GetLatestSnapshotsAsync(architecture.Id, 2, cancellationToken);
            var tail = latest.ElementAtOrDefault(0);
            var beforeTail = latest.ElementAtOrDefault(1);

            if (IsSameRun(tail, analysis) && IsSameRun(beforeTail, analysis))
            {
                tail!.Version = architecture.Version;
                tail.Summary = JsonSerializer.Serialize(analysis.Summary);
                tail.Areas = SnapshotAreas.Write(analysis.Areas);
                tail.CreatedAt = architecture.UpdatedAt;

                await _repository.SaveChangesAsync(cancellationToken);
                return;
            }

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

            var total = await _repository.CountSnapshotsAsync(architecture.Id, cancellationToken);
            if (total > MaxSnapshotsPerArchitecture)
            {
                var oldest = await _repository.GetOldestSnapshotsAsync(
                    architecture.Id, total - MaxSnapshotsPerArchitecture + 1, cancellationToken);

                _repository.RemoveSnapshots(oldest.Skip(1).ToList());
                await _repository.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception exception)
        {
            // A failed snapshot must not fail the save — but it must not fail silently either, or
            // the history stops recording and nobody finds out until someone misses their chart.
            _logger.LogWarning(exception,
                "Could not record a score snapshot for architecture {ArchitectureId} at version {Version}.",
                architecture.Id, architecture.Version);
        }
    }

    private static bool IsSameRun(ArchitectureSnapshot? snapshot, AnalysisResult analysis) =>
        snapshot is not null
        && snapshot.Score == analysis.Score
        && snapshot.ScoringVersion == analysis.ScoringVersion;

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

    // Verbatim, and that is a decision rather than an omission: the jsonb is a tolerant store so a
    // save never destroys what an older schema wrote. Two tests pin it —
    // Handle_ConfigurationWithFieldsRetiredInV3_IsStoredUntouched and, over HTTP,
    // Put_NodeConfiguration_RoundTripsAsJsonObject. See S14 in the security audit for why storing
    // only the known keys was tried and rejected.
    private static string ToStoredConfiguration(JsonElement? configuration) =>
        configuration is { } value && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value.GetRawText()
            : Node.EmptyConfiguration;

    private void ValidateConnectionConfigurations(List<ConnectionDto> connections)
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
                // System.Text.Json's message names internal types and paths; that detail belongs
                // in the log, and the client gets told what to fix, not how the parser is built.
                _logger.LogWarning(ex, "Rejected the configuration of connection {ConnectionId}.", connection.Id);
                throw new InvalidConnectionConfigurationException(connection.Id, "the value does not fit the resilience schema.");
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

            if (ConfigurationRanges.ZoneViolation(node.Type, configuration) is { } zoneViolation)
                throw new InvalidNodeConfigurationException(node.Name, zoneViolation);

            if (!_configurationRegistry.TryGetProvider(node.Type, out var provider))
                continue;

            object parsed;
            try
            {
                parsed = provider.Deserialize(configuration);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Rejected the configuration of node {NodeName} ({NodeType}).", node.Name, node.Type);
                throw new InvalidNodeConfigurationException(node.Name, $"the value does not fit the {node.Type} schema.");
            }

            if (ConfigurationRanges.Violation(parsed) is { } violation)
                throw new InvalidNodeConfigurationException(node.Name, violation);
        }
    }
}
