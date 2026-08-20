using arch_forge_backend.Application.Architectures;
using arch_forge_backend.Application.Architectures.DTOs;
using arch_forge_backend.Application.Common.Interfaces;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Challenges;

public class StartChallengeCommand
{
    public string Slug { get; set; } = string.Empty;
    /// <summary>
    /// V7 D1.2. The language the new architecture is named in, from <c>Accept-Language</c>. It is a
    /// property of the write and not of the reader: the name is persisted once and everybody who
    /// opens the link afterwards sees the same one.
    /// </summary>
    public string Locale { get; set; } = TextCatalog.DefaultLocale;

}

/// <summary>
/// V4 D2.3 and D4.3. Instantiates the challenge's starter architecture and hands back an ordinary
/// architecture with an ordinary share token.
///
/// <strong>Ordinary is the point.</strong> An attempt is not a special kind of entity (D2.2): it
/// saves, analyses, simulates and shares exactly like anything else, and the only thing that links
/// it to the challenge is the URL. That is what keeps V4 from needing a column, a table or a
/// migration for any of this.
/// </summary>
public class StartChallengeCommandHandler
{
    private readonly IArchitectureRepository _repository;
    private readonly ChallengeCatalog _catalog;
    private readonly TextCatalog _text;

    public StartChallengeCommandHandler(IArchitectureRepository repository, ChallengeCatalog catalog, TextCatalog text)
    {
        _repository = repository;
        _catalog = catalog;
        _text = text;
    }

    public async Task<ArchitectureDto?> Handle(StartChallengeCommand command, CancellationToken cancellationToken = default)
    {
        var definition = _catalog.Find(command.Slug);
        var starter = _catalog.StarterOf(command.Slug);

        if (definition is null || starter is null)
            return null;

        // V7 D1.2. The architecture's name is persisted user data, so it is written in a real
        // language at the moment it is created rather than kept as a key: a row in the database
        // that says `challenge.payments-friday.title` is a name nobody can read. This is why the
        // backend catalogue holds challenge titles even though it renders no other challenge prose.
        var architecture = starter.Instantiate(_text.Render(definition.TitleKey, command.Locale));

        await _repository.AddAsync(architecture, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return architecture.ToDto();
    }
}
