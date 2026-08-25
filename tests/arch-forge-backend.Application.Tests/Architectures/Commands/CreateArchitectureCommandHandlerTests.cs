using arch_forge_backend.Application.Architectures.Commands;
using arch_forge_backend.Application.Tests.TestDoubles;
using FluentValidation;
using Xunit;

namespace arch_forge_backend.Application.Tests.Architectures.Commands;

public class CreateArchitectureCommandHandlerTests
{
    private readonly FakeArchitectureRepository _repository = new();
    private readonly CreateArchitectureCommandHandler _handler;

    public CreateArchitectureCommandHandlerTests()
    {
        _handler = new CreateArchitectureCommandHandler(_repository, new CreateArchitectureCommandValidator());
    }

    [Fact]
    public async Task Handle_ValidName_CreatesArchitectureAtVersion1()
    {
        var result = await _handler.Handle(new CreateArchitectureCommand { Name = "My architecture" });

        Assert.Equal("My architecture", result.Name);
        Assert.Equal(1, result.Version);
        Assert.NotEmpty(result.ShareToken);
        Assert.NotEmpty(result.EditToken);
        Assert.NotEqual(result.ShareToken, result.EditToken);
        Assert.Equal(result.CreatedAt, result.UpdatedAt);
    }

    [Fact]
    public async Task Handle_EmptyName_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _handler.Handle(new CreateArchitectureCommand { Name = "" }));
    }
}
