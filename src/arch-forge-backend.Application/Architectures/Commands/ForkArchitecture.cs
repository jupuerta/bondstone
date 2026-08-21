using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Interfaces;
using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Architectures.Commands;

public class ForkArchitectureCommand
{
    public string ShareToken { get; set; } = string.Empty;
}

/// <summary>
/// V4 D2.3. Copies an architecture into a new one with its own tokens.
///
/// The smallest piece of V4 and the one that pays two debts at once. The §11 of the ideas document
/// promised that whoever opens a shared link can <em>modify</em> the architecture, and until now
/// opening <c>/a/{token}</c> edited the original - so "try it yourself" meant "edit mine". And it
/// is what starting a challenge from someone else's solution needs (D6.4): comparing is opening two
/// links, and improving on one is forking it.
///
/// Resolved by <see cref="ForkArchitectureCommand.ShareToken"/> and not by id: forking is available
/// from the read side, to anyone who can already see the architecture - the copy is what turns
/// "edit mine" back into "try it yourself", so it cannot itself require an edit token.
///
/// Ids are minted fresh for every node and edge, because they are primary keys and two
/// architectures cannot share them. That is exactly why a challenge's faults point at components
/// by name and not by id (<see cref="Challenges.ScenarioFault"/>).
/// </summary>
public class ForkArchitectureCommandHandler
{
    private readonly IArchitectureRepository _repository;

    public ForkArchitectureCommandHandler(IArchitectureRepository repository)
    {
        _repository = repository;
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
            ShareToken = Guid.NewGuid().ToString("N")[..10],
            EditToken = Guid.NewGuid().ToString("N"),
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
                Protocol = edge.Protocol,
                Synchronous = edge.Synchronous,
                Configuration = edge.Configuration
            });
        }

        await _repository.AddAsync(fork, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return fork.ToCreatedDto();
    }
}
