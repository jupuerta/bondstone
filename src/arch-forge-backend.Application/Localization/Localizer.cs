namespace arch_forge_backend.Application.Localization;

/// <summary>
/// V7 D1.2. A <see cref="TextCatalog"/> and the language to read it in, carried together.
///
/// It exists so that a component that writes prose - today only
/// <see cref="Export.MarkdownExporter"/> - takes <em>one</em> parameter instead of threading a
/// catalogue and a locale string through every private method. Passing the pair separately is how
/// one of them eventually gets forgotten and half a document comes out in the default language.
/// </summary>
public sealed record Localizer(TextCatalog Catalog, string Locale)
{
    public string Of(LocalizedText text) => Catalog.Render(text, Locale);

    public string Of(string key) => Catalog.Render(key, Locale);

    public string Of(string key, params (string Name, object Value)[] args) =>
        Catalog.Render(LocalizedText.Of(key, args), Locale);
}
