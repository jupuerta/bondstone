using arch_forge_backend.Application.Common.Interfaces;

namespace arch_forge_backend.Application.Architectures.Commands;

public class DeleteArchitectureCommand
{
    public string EditToken { get; set; } = string.Empty;
}

public class DeleteArchitectureCommandHandler
{
    private readonly IArchitectureRepository _repository;

    public DeleteArchitectureCommandHandler(IArchitectureRepository repository)
    {
        _repository = repository;
    }

    public async Task<bool> Handle(DeleteArchitectureCommand command, CancellationToken cancellationToken = default)
    {
        var architecture = await _repository.GetByEditTokenAsync(command.EditToken, cancellationToken);
        if (architecture is null)
            return false;

        _repository.Remove(architecture);
        await _repository.SaveChangesAsync(cancellationToken);

        return true;
    }
}
