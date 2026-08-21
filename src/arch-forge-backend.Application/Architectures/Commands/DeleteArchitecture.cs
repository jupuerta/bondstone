using arch_forge_backend.Application.Common.Interfaces;

namespace arch_forge_backend.Application.Architectures.Commands;

public class DeleteArchitectureCommand
{
    public string EditToken { get; set; } = string.Empty;
}

/// <summary>
/// Deletes an architecture and, by cascade, its whole graph.
///
/// <para><strong>By edit token and not by share token</strong>, like every other operation that
/// writes: the share token is the read handle everyone who has seen the architecture holds, and the
/// edit token is the one credential that only its owner does. Resolving this by the database id, as
/// it used to, made the id itself a write credential the moment any <c>GET</c> handed it back.</para>
///
/// <para><strong>Not versioned</strong>, unlike the PUT. The <c>409</c> of
/// <see cref="UpdateArchitectureCommandHandler"/> exists to stop a stale snapshot silently
/// overwriting somebody else's edit - there are two versions of the graph and only one can win.
/// A delete has nothing to merge: the intent is "this should not exist", and it is not made any
/// less true by somebody having moved a box a second earlier. Demanding a version would only mean
/// the caller has to read the architecture first in order to throw it away.</para>
///
/// <para>It is irreversible and there is no history (D6.6 keeps pushing it to V4), which is why the
/// endpoint answers <c>404</c> rather than pretending a second delete succeeded: on a destructive
/// operation with no undo, "there was nothing there" is information worth returning.</para>
/// </summary>
public class DeleteArchitectureCommandHandler
{
    private readonly IArchitectureRepository _repository;

    public DeleteArchitectureCommandHandler(IArchitectureRepository repository)
    {
        _repository = repository;
    }

    /// <returns><c>false</c> when the architecture does not exist, which the controller maps to 404.</returns>
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
