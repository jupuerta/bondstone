using System.Text.Json;
using arch_forge_backend.Application.Architectures;
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

        _handler = new UpdateArchitectureCommandHandler(
            _repository,
            registry,
            new UpdateArchitectureCommandValidator(),
            new ArchitectureAnalyzer([new SpofRule(), new MissingResilienceRule(), new NoObservabilityRule()]));
    }

    [Fact]
    public async Task Handle_ArchitectureDoesNotExist_ReturnsNull()
    {
        var result = await _handler.Handle(new UpdateArchitectureCommand { EditToken = "unknown-token", Name = "X", Version = 1 });

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_StaleVersion_ThrowsConflictWithCurrentVersion()
    {
        var architecture = await SeedArchitecture();

        var exception = await Assert.ThrowsAsync<ArchitectureConcurrencyConflictException>(() =>
            _handler.Handle(new UpdateArchitectureCommand { EditToken = architecture.EditToken, Name = "Renamed", Version = architecture.Version + 1 }));

        Assert.Equal(architecture.Version, exception.CurrentVersion);
    }

    [Fact]
    public async Task Handle_MatchingVersion_IncrementsVersionAndPersistsChanges()
    {
        var architecture = await SeedArchitecture();
        var originalVersion = architecture.Version;
        var command = new UpdateArchitectureCommand
        {
            EditToken = architecture.EditToken,
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
            EditToken = architecture.EditToken,
            Name = "Original",
            Version = architecture.Version,
            Nodes = [kept, dropped]
        });

        await _handler.Handle(new UpdateArchitectureCommand
        {
            EditToken = architecture.EditToken,
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
            EditToken = architecture.EditToken,
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
            EditToken = architecture.EditToken,
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
            EditToken = architecture.EditToken,
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
            EditToken = architecture.EditToken,
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
            EditToken = architecture.EditToken,
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
            EditToken = architecture.EditToken,
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
        var architecture = await SeedArchitecture();
        var node = Node(NodeType.Queue, "Orders queue");
        const string legacy = """{"durable":true,"deadLetterQueue":false,"maxRetries":3}""";
        node.Configuration = Json(legacy);

        var command = new UpdateArchitectureCommand
        {
            EditToken = architecture.EditToken,
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
        var architecture = await SeedArchitecture();
        var api = Node(NodeType.Api, "Api");
        var database = Node(NodeType.Database, "Db");
        const string resilience = """{"timeoutSeconds":5,"retries":2,"circuitBreaker":true,"fallback":false}""";

        var command = new UpdateArchitectureCommand
        {
            EditToken = architecture.EditToken,
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
            EditToken = architecture.EditToken,
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
            EditToken = architecture.EditToken,
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
            EditToken = architecture.EditToken,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [api, database],
            Connections = [Connect(api, database, Json("""{"timeoutSeconds":0}"""))]
        };

        await Assert.ThrowsAsync<InvalidConnectionConfigurationException>(() => _handler.Handle(command));
    }


    [Fact]
    public async Task Handle_NegativeInstances_ThrowsInvalidNodeConfigurationException()
    {
        var architecture = await SeedArchitecture();
        var api = Node(NodeType.Api, "Api");
        api.Configuration = Json("""{"instances":-3}""");

        var exception = await Assert.ThrowsAsync<InvalidNodeConfigurationException>(() =>
            _handler.Handle(Command(architecture, api)));

        Assert.Contains("instances must be 1 or greater", exception.Message);
    }

    [Fact]
    public async Task Handle_NegativeRequestsPerSecond_ThrowsInvalidNodeConfigurationException()
    {
        var architecture = await SeedArchitecture();
        var api = Node(NodeType.Api, "Api");
        api.Configuration = Json("""{"requestsPerSecond":-5000}""");

        var exception = await Assert.ThrowsAsync<InvalidNodeConfigurationException>(() =>
            _handler.Handle(Command(architecture, api)));

        Assert.Contains("requestsPerSecond must be 0 or greater", exception.Message);
    }

    [Fact]
    public async Task Handle_HitRatioAboveOne_ThrowsInvalidNodeConfigurationException()
    {
        var architecture = await SeedArchitecture();
        var cache = Node(NodeType.Cache, "Cache");
        cache.Configuration = Json("""{"hitRatio":5}""");

        await Assert.ThrowsAsync<InvalidNodeConfigurationException>(() => _handler.Handle(Command(architecture, cache)));
    }

    [Fact]
    public async Task Handle_NegativeReplicas_ThrowsInvalidNodeConfigurationException()
    {
        var architecture = await SeedArchitecture();
        var db = Node(NodeType.Database, "Db");
        db.Configuration = Json("""{"replicas":-3}""");

        await Assert.ThrowsAsync<InvalidNodeConfigurationException>(() => _handler.Handle(Command(architecture, db)));
    }

    [Fact]
    public async Task Handle_ZoneOutsideTheClosedSet_ThrowsInvalidNodeConfigurationException()
    {
        var architecture = await SeedArchitecture();
        var api = Node(NodeType.Api, "Api");
        api.Configuration = Json("""{"zone":"d"}""");

        var exception = await Assert.ThrowsAsync<InvalidNodeConfigurationException>(() =>
            _handler.Handle(Command(architecture, api)));

        Assert.Contains("zone must be one of a, b, c", exception.Message);
    }

    [Fact]
    public async Task Handle_ZoneOnATypeNobodyPlaces_ThrowsEvenWithoutASchemaProvider()
    {
        var architecture = await SeedArchitecture();
        var cdn = Node(NodeType.Cdn, "Cdn");
        cdn.Configuration = Json("""{"zone":"a"}""");

        var exception = await Assert.ThrowsAsync<InvalidNodeConfigurationException>(() =>
            _handler.Handle(Command(architecture, cdn)));

        Assert.Contains("zone does not apply to Cdn", exception.Message);
    }

    [Fact]
    public async Task Handle_ValidZone_IsStoredWithTheRestOfTheConfiguration()
    {
        var architecture = await SeedArchitecture();
        var api = Node(NodeType.Api, "Api");
        api.Configuration = Json("""{"zone":"a","instances":2}""");

        var result = await _handler.Handle(Command(architecture, api));

        Assert.NotNull(result);
        var persisted = await _repository.GetByIdAsync(architecture.Id);
        Assert.Contains("\"zone\":\"a\"", Assert.Single(persisted!.Nodes).Configuration);
    }

    [Fact]
    public async Task Handle_NodeValuesAtTheEdgeOfTheirRange_Succeed()
    {
        var architecture = await SeedArchitecture();
        var api = Node(NodeType.Api, "Api");
        api.Configuration = Json("""{"instances":1,"requestsPerSecond":0,"rateLimit":1}""");
        var cache = Node(NodeType.Cache, "Cache");
        cache.Configuration = Json("""{"hitRatio":0}""");
        var db = Node(NodeType.Database, "Db");
        db.Configuration = Json("""{"replicas":0,"readReplicas":0}""");

        var result = await _handler.Handle(Command(architecture, api, cache, db));

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Handle_TimeoutOverTheCeiling_Throws()
    {
        var architecture = await SeedArchitecture();
        var api = Node(NodeType.Api, "Api");
        var database = Node(NodeType.Database, "Db");

        var command = new UpdateArchitectureCommand
        {
            EditToken = architecture.EditToken,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [api, database],
            Connections = [Connect(api, database, Json("""{"timeoutSeconds":100000}"""))]
        };

        var exception = await Assert.ThrowsAsync<InvalidConnectionConfigurationException>(() => _handler.Handle(command));

        Assert.Contains("timeoutSeconds must be between 1 and 600", exception.Message);
    }

    [Fact]
    public async Task Handle_TimeoutAtTheCeiling_Succeeds()
    {
        var architecture = await SeedArchitecture();
        var api = Node(NodeType.Api, "Api");
        var database = Node(NodeType.Database, "Db");

        var command = new UpdateArchitectureCommand
        {
            EditToken = architecture.EditToken,
            Name = "Renamed",
            Version = architecture.Version,
            Nodes = [api, database],
            Connections = [Connect(api, database, Json("""{"timeoutSeconds":600,"retries":0}"""))]
        };

        Assert.NotNull(await _handler.Handle(command));
    }

    private static UpdateArchitectureCommand Command(Architecture architecture, params NodeDto[] nodes) => new()
    {
        EditToken = architecture.EditToken,
        Name = "Renamed",
        Version = architecture.Version,
        Nodes = [.. nodes]
    };

    [Fact]
    public async Task Handle_SelfLoop_IsRejected()
    {
        var architecture = await SeedArchitecture();
        var service = Node(NodeType.Service, "Orders");

        var command = new UpdateArchitectureCommand
        {
            EditToken = architecture.EditToken,
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
            EditToken = architecture.EditToken,
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
            EditToken = architecture.EditToken,
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
            EditToken = Guid.NewGuid().ToString("N"),
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
        var architecture = Seed();

        await _handler.Handle(Command(architecture, "Two boxes"));

        var snapshot = Assert.Single(_repository.Snapshots);
        Assert.Equal(architecture.Id, snapshot.ArchitectureId);
        Assert.Equal(2, snapshot.Version);
        Assert.Equal("v12", snapshot.ScoringVersion);
    }

    [Fact]
    public async Task Handle_RecordsTheAreasBehindTheNumberAndNotOnlyTheNumber()
    {
        var architecture = Seed();

        await _handler.Handle(Command(architecture, "Api on a database"));

        var snapshot = Assert.Single(_repository.Snapshots);
        var stored = SnapshotAreas.Read(snapshot.Areas);

        Assert.NotNull(stored);

        Assert.Equal([Area.Availability, Area.Resilience, Area.Observability], stored.Keys);
        Assert.All(stored.Values, score => Assert.InRange(score, 0, 100));
    }

    [Fact]
    public async Task Handle_SavingTheSameVersionTwiceRecordsItOnce()
    {
        var architecture = Seed();

        await _handler.Handle(Command(architecture, "Once"));
        await _repository.RecordSnapshotAsync(new ArchitectureSnapshot
        {
            Id = Guid.NewGuid(),
            ArchitectureId = architecture.Id,
            Version = 2,
            ScoringVersion = "v5",
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
            EditToken = Guid.NewGuid().ToString("N"),
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
            EditToken = architecture.EditToken,
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
