using arch_forge_backend.Application.Architectures.Commands;
using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Exceptions;
using arch_forge_backend.Application.Configuration;
using arch_forge_backend.Application.Configuration.Schemas;
using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;
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
            new NodeConfigurationProvider<QueueConfiguration>(NodeType.Queue)
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
        var command = new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [new NodeDto { Type = NodeType.Service, Name = "Orders", PositionX = 1, PositionY = 2 }]
        };

        var result = await _handler.Handle(command);

        Assert.NotNull(result);
        Assert.Equal(architecture.Version + 1, result!.Version);

        var persisted = await _repository.GetByIdAsync(architecture.Id);
        Assert.Equal("Renamed", persisted!.Name);
        Assert.Single(persisted.Nodes);
    }

    [Fact]
    public async Task Handle_InvalidQueueConfiguration_ThrowsInvalidNodeConfigurationException()
    {
        var architecture = await SeedArchitecture();
        var command = new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [new NodeDto { Type = NodeType.Queue, Name = "Orders queue", Configuration = "{ not valid json" }]
        };

        await Assert.ThrowsAsync<InvalidNodeConfigurationException>(() => _handler.Handle(command));
    }

    [Fact]
    public async Task Handle_ValidQueueConfiguration_Succeeds()
    {
        var architecture = await SeedArchitecture();
        var command = new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [new NodeDto { Type = NodeType.Queue, Name = "Orders queue", Configuration = """{"durable":true,"deadLetterQueue":false,"maxRetries":3}""" }]
        };

        var result = await _handler.Handle(command);

        Assert.NotNull(result);
    }

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
