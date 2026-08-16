using arch_forge_backend.Application.Architectures;
using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Interfaces;

namespace arch_forge_backend.Application.Challenges;

public class OpenChallengeSolutionCommand
{
    public string Slug { get; set; } = string.Empty;
}

/// <summary>
/// V4 D3.5 and D6.4. Hands the reference solution over as <strong>an architecture, not a picture</strong>.
///
/// A read-only diagram would answer "what does it look like"; an architecture answers the question
/// the product is actually about — open it, analyse it, kill its database, see why it holds. It is
/// the same move D6.4 makes for comparing two people's solutions: comparing is opening two links,
/// and here one of the two is the author's.
///
/// It instantiates a fresh copy rather than pointing at a shared one, for the same reason
/// <see cref="StartChallengeCommandHandler"/> does: whoever opens it can take it apart without
/// touching what the next person sees.
///
/// <strong>Nothing stops a client from calling this before passing.</strong> There is no identity to
/// check it against (D2.1), and D3.6 already settled the posture: without a ranking, a user who
/// goes looking for the answer is a user learning at their own pace. The UI offers it once the
/// challenge is passed, which is where it teaches instead of replacing the attempt.
/// </summary>
public class OpenChallengeSolutionCommandHandler
{
    private readonly IArchitectureRepository _repository;
    private readonly ChallengeCatalog _catalog;

    public OpenChallengeSolutionCommandHandler(IArchitectureRepository repository, ChallengeCatalog catalog)
    {
        _repository = repository;
        _catalog = catalog;
    }

    public async Task<ArchitectureDto?> Handle(OpenChallengeSolutionCommand command, CancellationToken cancellationToken = default)
    {
        var definition = _catalog.Find(command.Slug);
        var solution = _catalog.ReferenceSolutionOf(command.Slug);

        if (definition is null || solution is null)
            return null;

        // Named so it cannot be mistaken for the attempt it is being compared against - two tabs of
        // the same editor with the same title would be a trap.
        var architecture = solution.Instantiate($"{definition.Title} — solución de referencia");

        await _repository.AddAsync(architecture, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return architecture.ToDto();
    }
}
