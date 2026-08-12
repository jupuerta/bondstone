using arch_forge_backend.Application.Architectures.Commands;
using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Domain.Entities;
using Xunit;

namespace arch_forge_backend.Application.Tests.Architectures.Commands;

public class DeleteArchitectureCommandHandlerTests
{
    private readonly FakeArchitectureRepository _repository = new();
    private readonly DeleteArchitectureCommandHandler _handler;

    public DeleteArchitectureCommandHandlerTests()
    {
        _handler = new DeleteArchitectureCommandHandler(_repository);
    }

    [Fact]
    public async Task Handle_ExistingArchitecture_RemovesItAndReportsSuccess()
    {
        var architecture = await SeedArchitecture();

        var deleted = await _handler.Handle(new DeleteArchitectureCommand { Id = architecture.Id });

        Assert.True(deleted);
        Assert.Null(await _repository.GetByIdAsync(architecture.Id));
    }

    [Fact]
    public async Task Handle_UnknownArchitecture_ReportsNothingDeleted()
    {
        // The controller turns this into a 404. On an irreversible operation with no history,
        // "there was nothing there" is worth returning rather than pretending it worked.
        var deleted = await _handler.Handle(new DeleteArchitectureCommand { Id = Guid.NewGuid() });

        Assert.False(deleted);
    }

    [Fact]
    public async Task Handle_DoesNotAskForAVersion()
    {
        // Unlike the PUT. The 409 exists to stop a stale snapshot silently overwriting somebody
        // else's edit; a delete has nothing to merge, and demanding a version would only mean
        // reading an architecture in order to throw it away.
        var architecture = await SeedArchitecture();
        architecture.Version = 47;

        Assert.True(await _handler.Handle(new DeleteArchitectureCommand { Id = architecture.Id }));
    }

    [Fact]
    public async Task Handle_TakesTheWholeGraphWithIt()
    {
        // The database does this by cascade; the fake does it by dropping the entry the nodes and
        // connections hang off. Either way, nothing of the architecture is left readable.
        var architecture = await SeedArchitecture();
        var api = new Node { Id = Guid.NewGuid(), Type = Domain.Enums.NodeType.Api, Name = "Api" };
        var db = new Node { Id = Guid.NewGuid(), Type = Domain.Enums.NodeType.Database, Name = "Db" };

        await _repository.ReplaceGraphAsync(
            architecture,
            [api, db],
            [new Connection { Id = Guid.NewGuid(), SourceNodeId = api.Id, TargetNodeId = db.Id }]);

        await _handler.Handle(new DeleteArchitectureCommand { Id = architecture.Id });

        Assert.Null(await _repository.GetByIdAsync(architecture.Id));
        Assert.Null(await _repository.GetByShareTokenAsync(architecture.ShareToken));
    }

    private async Task<Architecture> SeedArchitecture()
    {
        var architecture = new Architecture
        {
            Id = Guid.NewGuid(),
            Name = "Checkout",
            ShareToken = Guid.NewGuid().ToString("N")[..10],
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(architecture);
        await _repository.SaveChangesAsync();

        return architecture;
    }
}
