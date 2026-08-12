using arch_forge_backend.Application.Common.Interfaces;

namespace arch_forge_backend.Application.Architectures.Commands;

public class DeleteArchitectureCommand
{
    public Guid Id { get; set; }
}

/// <summary>
/// Deletes an architecture and, by cascade, its whole graph.
///
/// <para><strong>By id and not by share token</strong>, like every other operation that writes: the
/// token is the read handle that <c>GET</c> uses, and the id is what the client already holds after
/// creating or loading one.</para>
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
        var architecture = await _repository.GetByIdAsync(command.Id, cancellationToken);
        if (architecture is null)
            return false;

        _repository.Remove(architecture);
        await _repository.SaveChangesAsync(cancellationToken);

        return true;
    }
}
