using arch_forge_backend.Application.Architectures;
using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Interfaces;

namespace arch_forge_backend.Application.Challenges;

public class StartChallengeCommand
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
/// V4 D2.3 and D4.3. Instantiates the challenge's starter architecture and hands back an ordinary
/// architecture with an ordinary pair of tokens.
///
/// <strong>Ordinary is the point.</strong> An attempt is not a special kind of entity (D2.2): it
/// saves, analyses, simulates and shares exactly like anything else, and the only thing that links
/// it to the challenge is the URL. That is what keeps V4 from needing a column, a table or a
/// migration for any of this.
///
/// It hands back the edit token, unlike <see cref="OpenChallengeSolutionCommandHandler"/>: starting
/// a challenge is claiming an attempt as yours, and there is nobody else this could belong to yet.
/// </summary>
public class StartChallengeCommandHandler
{
    private readonly IArchitectureRepository _repository;
    private readonly ChallengeCatalog _catalog;

    public StartChallengeCommandHandler(IArchitectureRepository repository, ChallengeCatalog catalog)
    {
        _repository = repository;
        _catalog = catalog;
    }

    /// <summary>The client's name, or the slug when it sent none. Never empty.</summary>
    internal static string NameOr(string? name, string slug) =>
        string.IsNullOrWhiteSpace(name) ? slug : name.Trim();

    public async Task<ArchitectureCreatedDto?> Handle(StartChallengeCommand command, CancellationToken cancellationToken = default)
    {
        var definition = _catalog.Find(command.Slug);
        var starter = _catalog.StarterOf(command.Slug);

        if (definition is null || starter is null)
            return null;

        // V7 D1.2. The architecture's name is persisted user data, so it is written in a real
        // language at the moment it is created rather than kept as a key: a row in the database
        // that says `challenge.payments-friday.title` is a name nobody can read. This is why the
        // backend catalogue holds challenge titles even though it renders no other challenge prose.
        var architecture = starter.Instantiate(NameOr(command.Name, definition.Slug));

        await _repository.AddAsync(architecture, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return architecture.ToCreatedDto();
    }
}
