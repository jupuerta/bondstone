using arch_forge_backend.Application.Architectures;
using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Interfaces;

namespace arch_forge_backend.Application.Challenges;

public class OpenChallengeSolutionCommand
{
    public string Slug { get; set; } = string.Empty;
    /// <summary>
    /// V7 D2.1. The name for the new architecture, <strong>written by the client</strong>.
    ///
    /// It used to be the challenge's title rendered here, which is why the backend kept a catalogue
    /// of titles after everything else had become a key. But this is a persisted, user-editable
    /// column shared with hand-made architectures: a row saying <c>challenge.payments-friday.title</c>
    /// is a name nobody can read, and a row in a language chosen by a header is a name that lies to
    /// the next person who opens the link. The client knows the title it just showed, so it sends it.
    ///
    /// Empty falls back to the slug rather than to nothing: a nameless architecture is worse than an
    /// unlovely one, and this is the only value that cannot be recomputed later.
    /// </summary>
    public string? Name { get; set; }

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
///
/// <strong>Read-only by construction, unlike <see cref="StartChallengeCommandHandler"/>.</strong> It
/// still mints an edit token - every row needs one - but hands back the ordinary
/// <see cref="ArchitectureDto"/> rather than <see cref="ArchitectureCreatedDto"/>, so nobody, not
/// even the person who just opened it, receives the token that would let them change it. A
/// reference solution is something to read, not a second scratchpad.
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
        var architecture = solution.Instantiate(
            StartChallengeCommandHandler.NameOr(command.Name, $"{definition.Slug} — reference solution"));

        await _repository.AddAsync(architecture, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return architecture.ToDto();
    }
}
