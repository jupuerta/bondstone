using bondstone_backend.Application.Architectures.DTOs;
using bondstone_backend.Application.Common.Interfaces;
using bondstone_backend.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace bondstone_backend.Application.Architectures.Commands;

public class ForkArchitectureCommand
{
    public string ShareToken { get; set; } = string.Empty;
}

public class ForkArchitectureCommandHandler
{
    private readonly IArchitectureRepository _repository;
    private readonly ILogger<ForkArchitectureCommandHandler> _logger;

    public ForkArchitectureCommandHandler(IArchitectureRepository repository, ILogger<ForkArchitectureCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<ArchitectureCreatedDto?> Handle(ForkArchitectureCommand command, CancellationToken cancellationToken = default)
    {
        var source = await _repository.GetByShareTokenAsync(command.ShareToken, cancellationToken);
        if (source is null)
            return null;

        var now = DateTime.UtcNow;
        var fork = new Architecture
        {
            Id = Guid.NewGuid(),
            Name = source.Name,
            ShareToken = ArchitectureTokens.NewShareToken(),
            EditToken = ArchitectureTokens.NewEditToken(),
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        var idsBySource = source.Nodes.ToDictionary(n => n.Id, _ => Guid.NewGuid());

        foreach (var node in source.Nodes)
        {
            fork.Nodes.Add(new Node
            {
                Id = idsBySource[node.Id],
                ArchitectureId = fork.Id,
                Type = node.Type,
                Name = node.Name,
                PositionX = node.PositionX,
                PositionY = node.PositionY,
                Configuration = node.Configuration
            });
        }

        foreach (var edge in source.Connections)
        {
            fork.Connections.Add(new Connection
            {
                Id = Guid.NewGuid(),
                ArchitectureId = fork.Id,
                SourceNodeId = idsBySource[edge.SourceNodeId],
                TargetNodeId = idsBySource[edge.TargetNodeId],
                Synchronous = edge.Synchronous,
                Configuration = edge.Configuration
            });
        }

        await _repository.AddAsync(fork, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Forked architecture {SourceArchitectureId} into {ArchitectureId}.", source.Id, fork.Id);

        return fork.ToCreatedDto();
    }
}
