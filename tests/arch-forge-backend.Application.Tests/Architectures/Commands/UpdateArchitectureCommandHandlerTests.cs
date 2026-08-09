using System.Text.Json;
using arch_forge_backend.Application.Architectures.Commands;
using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Exceptions;
using arch_forge_backend.Application.Configuration;
using arch_forge_backend.Application.Configuration.Schemas;
using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;
using FluentValidation;
using Xunit;

namespace arch_forge_backend.Application.Tests.Architectures.Commands;

public class UpdateArchitectureCommandHandlerTests
{
    private readonly FakeArchitectureRepository _repository = new();
    private readonly UpdateArchitectureCommandHandler _handler;

    public UpdateArchitectureCommandHandlerTests()
    {
        var registry = new NodeConfigurationRegistry([
            new NodeConfigurationProvider<ApiConfiguration>(NodeType.Api),
            new NodeConfigurationProvider<DatabaseConfiguration>(NodeType.Database),
            new NodeConfigurationProvider<ServiceConfiguration>(NodeType.Service),
            new NodeConfigurationProvider<QueueConfiguration>(NodeType.Queue),
            new NodeConfigurationProvider<CacheConfiguration>(NodeType.Cache)
        ]);

        _handler = new UpdateArchitectureCommandHandler(_repository, registry, new UpdateArchitectureCommandValidator());
    }

    [Fact]
    public async Task Handle_ArchitectureDoesNotExist_ReturnsNull()
    {
        var result = await _handler.Handle(new UpdateArchitectureCommand { Id = Guid.NewGuid(), Name = "X", Version = 1 });

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_StaleVersion_ThrowsConflictWithCurrentVersion()
    {
        var architecture = await SeedArchitecture();

        var exception = await Assert.ThrowsAsync<ArchitectureConcurrencyConflictException>(() =>
            _handler.Handle(new UpdateArchitectureCommand { Id = architecture.Id, Name = "Renamed", Version = architecture.Version + 1 }));

        Assert.Equal(architecture.Version, exception.CurrentVersion);
    }

    [Fact]
    public async Task Handle_MatchingVersion_IncrementsVersionAndPersistsChanges()
    {
        var architecture = await SeedArchitecture();
        var originalVersion = architecture.Version;
        var command = new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = originalVersion,
            Nodes = [Node(NodeType.Service, "Orders", x: 1, y: 2)]
        };

        var result = await _handler.Handle(command);

        Assert.NotNull(result);
        Assert.Equal(originalVersion + 1, result!.Version);

        var persisted = await _repository.GetByIdAsync(architecture.Id);
        Assert.Equal("Renamed", persisted!.Name);
        var node = Assert.Single(persisted.Nodes);
        Assert.Equal(1, node.PositionX);
        Assert.Equal(2, node.PositionY);
    }

    [Fact]
    public async Task Handle_ReplacesPreviousGraphInsteadOfMerging()
    {
        var architecture = await SeedArchitecture();
        var kept = Node(NodeType.Api, "Api");
        var dropped = Node(NodeType.Database, "Db");

        await _handler.Handle(new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Original",
            Version = architecture.Version,
            Nodes = [kept, dropped]
        });

        await _handler.Handle(new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Original",
            Version = architecture.Version,
            Nodes = [kept]
        });

        var persisted = await _repository.GetByIdAsync(architecture.Id);
        Assert.Equal(kept.Id, Assert.Single(persisted!.Nodes).Id);
    }

    [Fact]
    public async Task Handle_ConnectionReferencingUnknownNode_ThrowsValidationException()
    {
        var architecture = await SeedArchitecture();
        var api = Node(NodeType.Api, "Api");

        var command = new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [api],
            Connections =
            [
                new ConnectionDto
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = api.Id,
                    TargetNodeId = Guid.NewGuid(),
                    Protocol = ProtocolType.Http,
                    Synchronous = true
                }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _handler.Handle(command));
    }

    [Fact]
    public async Task Handle_DuplicateNodeIds_ThrowsValidationException()
    {
        var architecture = await SeedArchitecture();
        var id = Guid.NewGuid();

        var command = new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [Node(NodeType.Api, "Api", id: id), Node(NodeType.Service, "Orders", id: id)]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _handler.Handle(command));
    }

    [Fact]
    public async Task Handle_NodeWithoutId_ThrowsValidationException()
    {
        var architecture = await SeedArchitecture();

        var command = new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [Node(NodeType.Api, "Api", id: Guid.Empty)]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _handler.Handle(command));
    }

    [Fact]
    public async Task Handle_InvalidQueueConfiguration_ThrowsInvalidNodeConfigurationException()
    {
        var architecture = await SeedArchitecture();
        var node = Node(NodeType.Queue, "Orders queue");
        node.Configuration = Json("""{"durable":"not-a-boolean"}""");

        var command = new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [node]
        };

        await Assert.ThrowsAsync<InvalidNodeConfigurationException>(() => _handler.Handle(command));
    }

    [Fact]
    public async Task Handle_ConfigurationThatIsNotAnObject_ThrowsInvalidNodeConfigurationException()
    {
        var architecture = await SeedArchitecture();
        var node = Node(NodeType.Service, "Orders");
        node.Configuration = Json("\"just a string\"");

        var command = new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [node]
        };

        await Assert.ThrowsAsync<InvalidNodeConfigurationException>(() => _handler.Handle(command));
    }

    [Fact]
    public async Task Handle_ValidQueueConfiguration_Succeeds()
    {
        var architecture = await SeedArchitecture();
        var node = Node(NodeType.Queue, "Orders queue");
        node.Configuration = Json("""{"durable":true,"deadLetterQueue":false,"maxRetries":3}""");

        var command = new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [node]
        };

        var result = await _handler.Handle(command);

        Assert.NotNull(result);
        Assert.Equal("""{"durable":true,"deadLetterQueue":false,"maxRetries":3}""",
            (await _repository.GetByIdAsync(architecture.Id))!.Nodes[0].Configuration);
    }

    private static NodeDto Node(NodeType type, string name, double x = 0, double y = 0, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Type = type,
        Name = name,
        Position = new PositionDto { X = x, Y = y }
    };

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private async Task<Architecture> SeedArchitecture()
    {
        var architecture = new Architecture
        {
            Id = Guid.NewGuid(),
            Name = "Original",
            ShareToken = Guid.NewGuid().ToString("N")[..10],
            Version = 3,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(architecture);
        return architecture;
    }
}
