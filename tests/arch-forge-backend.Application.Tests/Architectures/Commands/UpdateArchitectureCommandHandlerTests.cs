using System.Text.Json;
using arch_forge_backend.Application.Architectures.Commands;
using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Exceptions;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
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
            new NodeConfigurationProvider<CacheConfiguration>(NodeType.Cache),
            new NodeConfigurationProvider<GatewayConfiguration>(NodeType.Gateway),
            new NodeConfigurationProvider<LoadBalancerConfiguration>(NodeType.LoadBalancer)
        ]);

        // The real seven-plus-eight analyzer: V4 D5.4 records what the architecture scored on
        // every save, so this handler now runs it.
        _handler = new UpdateArchitectureCommandHandler(
            _repository,
            registry,
            new UpdateArchitectureCommandValidator(),
            new ArchitectureAnalyzer([new SpofRule(), new MissingResilienceRule(), new NoObservabilityRule()]));
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
        node.Configuration = Json("""{"maxRetries":"not-a-number"}""");

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
        node.Configuration = Json("""{"maxRetries":3}""");

        var command = new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [node]
        };

        var result = await _handler.Handle(command);

        Assert.NotNull(result);
        Assert.Equal("""{"maxRetries":3}""",
            (await _repository.GetByIdAsync(architecture.Id))!.Nodes[0].Configuration);
    }

    [Fact]
    public async Task Handle_ConfigurationWithFieldsRetiredInV3_IsStoredUntouched()
    {
        // V3 plan técnico §4, and the whole reason the 16 fields cost no migration: the schema is
        // permissive with unknown properties (NodeConfigurationProvider, no UnmappedMemberHandling
        // .Disallow) and ToStoredConfiguration keeps the JSON exactly as it arrived. A row written
        // before V3 keeps durable and deadLetterQueue, nobody reads them, and nothing breaks.
        var architecture = await SeedArchitecture();
        var node = Node(NodeType.Queue, "Orders queue");
        const string legacy = """{"durable":true,"deadLetterQueue":false,"maxRetries":3}""";
        node.Configuration = Json(legacy);

        var command = new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [node]
        };

        var result = await _handler.Handle(command);

        Assert.NotNull(result);
        Assert.Equal(legacy, (await _repository.GetByIdAsync(architecture.Id))!.Nodes[0].Configuration);
    }

    [Fact]
    public async Task Handle_ConnectionResilienceConfiguration_IsPersistedAsGiven()
    {
        // V2 D3.1: timeoutSeconds and retries live inside the edge's jsonb, next to the three
        // mechanisms that used to have nowhere to go.
        var architecture = await SeedArchitecture();
        var api = Node(NodeType.Api, "Api");
        var database = Node(NodeType.Database, "Db");
        const string resilience = """{"timeoutSeconds":5,"retries":2,"circuitBreaker":true,"fallback":false}""";

        var command = new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [api, database],
            Connections = [Connect(api, database, Json(resilience))]
        };

        var result = await _handler.Handle(command);

        Assert.NotNull(result);
        Assert.Equal(resilience, (await _repository.GetByIdAsync(architecture.Id))!.Connections[0].Configuration);
    }

    [Fact]
    public async Task Handle_ConnectionWithoutConfiguration_IsStoredAsEmptyObject()
    {
        var architecture = await SeedArchitecture();
        var api = Node(NodeType.Api, "Api");
        var database = Node(NodeType.Database, "Db");

        await _handler.Handle(new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [api, database],
            Connections = [Connect(api, database)]
        });

        Assert.Equal("{}", (await _repository.GetByIdAsync(architecture.Id))!.Connections[0].Configuration);
    }

    [Fact]
    public async Task Handle_ConnectionConfigurationThatDoesNotMatchSchema_Throws()
    {
        var architecture = await SeedArchitecture();
        var api = Node(NodeType.Api, "Api");
        var database = Node(NodeType.Database, "Db");

        var command = new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [api, database],
            Connections = [Connect(api, database, Json("""{"timeoutSeconds":"not-a-number"}"""))]
        };

        await Assert.ThrowsAsync<InvalidConnectionConfigurationException>(() => _handler.Handle(command));
    }

    [Fact]
    public async Task Handle_NonPositiveTimeout_Throws()
    {
        var architecture = await SeedArchitecture();
        var api = Node(NodeType.Api, "Api");
        var database = Node(NodeType.Database, "Db");

        var command = new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [api, database],
            Connections = [Connect(api, database, Json("""{"timeoutSeconds":0}"""))]
        };

        await Assert.ThrowsAsync<InvalidConnectionConfigurationException>(() => _handler.Handle(command));
    }

    [Fact]
    public async Task Handle_SelfLoop_IsRejected()
    {
        // V2 D3.4: a self-loop corrupts the graph, so it is rejected on save. A cycle between two
        // or more nodes is not - that one the analyzer has an opinion about.
        var architecture = await SeedArchitecture();
        var service = Node(NodeType.Service, "Orders");

        var command = new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [service],
            Connections = [Connect(service, service)]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _handler.Handle(command));
    }

    [Fact]
    public async Task Handle_CycleBetweenTwoNodes_IsAccepted()
    {
        var architecture = await SeedArchitecture();
        var orders = Node(NodeType.Service, "Orders");
        var payments = Node(NodeType.Service, "Payments");

        var result = await _handler.Handle(new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [orders, payments],
            Connections = [Connect(orders, payments), Connect(payments, orders)]
        });

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Handle_GatewayAndLoadBalancerNodes_AreAccepted()
    {
        var architecture = await SeedArchitecture();

        var result = await _handler.Handle(new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [Node(NodeType.Gateway, "Gateway"), Node(NodeType.LoadBalancer, "LB")]
        });

        Assert.NotNull(result);
        Assert.Equal(2, (await _repository.GetByIdAsync(architecture.Id))!.Nodes.Count);
    }

    private static ConnectionDto Connect(NodeDto source, NodeDto target, JsonElement? configuration = null) => new()
    {
        Id = Guid.NewGuid(),
        SourceNodeId = source.Id,
        TargetNodeId = target.Id,
        Protocol = ProtocolType.Http,
        Synchronous = true,
        Configuration = configuration
    };

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

    [Fact]
    public async Task Handle_RecordsWhatTheArchitectureScoredAtThisVersion()
    {
        // V4 D5.4. On save and not on analysis: the live analysis fires on a debounce while you
        // type, and recording there would fill the table with half-finished edits.
        var architecture = Seed();

        await _handler.Handle(Command(architecture, "Two boxes"));

        var snapshot = Assert.Single(_repository.Snapshots);
        Assert.Equal(architecture.Id, snapshot.ArchitectureId);
        Assert.Equal(2, snapshot.Version);
        Assert.Equal("v4", snapshot.ScoringVersion);
    }

    [Fact]
    public async Task Handle_SavingTheSameVersionTwiceRecordsItOnce()
    {
        // The autosave can retry, and a snapshot is a fact about a version rather than about an
        // attempt to save it - which is what the unique index on (architecture, version) enforces.
        var architecture = Seed();

        await _handler.Handle(Command(architecture, "Once"));
        await _repository.RecordSnapshotAsync(new ArchitectureSnapshot
        {
            Id = Guid.NewGuid(),
            ArchitectureId = architecture.Id,
            Version = 2,
            ScoringVersion = "v4",
            CreatedAt = DateTime.UtcNow
        });

        Assert.Single(_repository.Snapshots);
    }

    private Architecture Seed()
    {
        var architecture = new Architecture
        {
            Id = Guid.NewGuid(),
            Name = "Seed",
            ShareToken = "tok",
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _repository.Seed(architecture);
        return architecture;
    }

    private static UpdateArchitectureCommand Command(Architecture architecture, string name)
    {
        var api = Guid.NewGuid();
        var db = Guid.NewGuid();

        return new UpdateArchitectureCommand
        {
            Id = architecture.Id,
            Name = name,
            Version = architecture.Version,
            Nodes =
            [
                new NodeDto { Id = api, Type = NodeType.Api, Name = "Api", Position = new PositionDto() },
                new NodeDto { Id = db, Type = NodeType.Database, Name = "Db", Position = new PositionDto() }
            ],
            Connections =
            [
                new ConnectionDto
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = api,
                    TargetNodeId = db,
                    Protocol = ProtocolType.Sql,
                    Synchronous = true
                }
            ]
        };
    }
}
