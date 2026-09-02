using arch_forge_backend.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace arch_forge_backend.Application.Architectures.Commands;

public class DeleteArchitectureCommand
{
    public string EditToken { get; set; } = string.Empty;
}

public class DeleteArchitectureCommandHandler
{
    private readonly IArchitectureRepository _repository;
    private readonly ILogger<DeleteArchitectureCommandHandler> _logger;

    public DeleteArchitectureCommandHandler(IArchitectureRepository repository, ILogger<DeleteArchitectureCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<bool> Handle(DeleteArchitectureCommand command, CancellationToken cancellationToken = default)
    {
        var architecture = await _repository.GetByEditTokenAsync(command.EditToken, cancellationToken);
        if (architecture is null)
            return false;

        _repository.Remove(architecture);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted architecture {ArchitectureId}.", architecture.Id);

        return true;
    }
}
