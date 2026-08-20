using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace arch_forge_backend.Application.Localization;

/// <summary>
/// V7 D1.2. Turns a <see cref="LocalizedText"/> into a sentence, in one language.
///
/// <strong>Why the backend still owns a catalogue at all.</strong> The panel resolves its own keys
/// client-side and never calls this. The exporter does: <c>POST /architectures/{id}/export</c>
/// writes <c>finding.Message</c> into a Markdown document server-side, so the day the contract
/// stops carrying prose, the exporter needs somewhere to get it from. This is that somewhere, and
/// it is the only reason it exists - which is why it lives next to nothing else and is injected
/// nowhere but into the export handler.
///
/// Loaded once at startup from embedded resources, same criterion as
/// <see cref="Challenges.ChallengeCatalog"/>: a missing catalogue is a deploy mistake, and the
/// moment to notice it is boot, not the first export.
/// </summary>
public sealed class TextCatalog
{
    private const string Prefix = "arch_forge_backend.Application.Localization.Resources.";

    /// <summary>
    /// What an unknown locale falls back to, and what a key missing from a translation falls back
    /// to before it gives up. Spanish because it is the language every sentence was written in.
    /// </summary>
    public const string DefaultLocale = "es";

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, CatalogEntry>> _byLocale;

    public TextCatalog()
    {
        var assembly = typeof(TextCatalog).Assembly;
        var byLocale = new Dictionary<string, IReadOnlyDictionary<string, CatalogEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
                     .Where(n => n.EndsWith(".json", StringComparison.Ordinal))
                     .OrderBy(n => n, StringComparer.Ordinal))
        {
            var locale = resource[Prefix.Length..^".json".Length];
            byLocale[locale] = Read(assembly, resource);
        }

        if (!byLocale.ContainsKey(DefaultLocale))
            throw new InvalidOperationException($"No text catalogue for the default locale '{DefaultLocale}'.");

        _byLocale = byLocale;
    }

    /// <summary>The locales that have a catalogue, ordered so the list does not shuffle between deploys.</summary>
    public IReadOnlyList<string> Locales => _byLocale.Keys.OrderBy(l => l, StringComparer.Ordinal).ToList();

    public bool Has(string locale, string key) =>
        _byLocale.TryGetValue(locale, out var entries) && entries.ContainsKey(key);

    /// <summary>
    /// The key resolved and its placeholders filled. A key the catalogue does not have falls back
    /// to <see cref="DefaultLocale"/> and, failing that, returns the key itself: an export missing
    /// one sentence is a document with a gap, and an export that throws is no document at all.
    /// </summary>
    public string Render(LocalizedText text, string locale)
    {
        var entry = Lookup(text.Key, locale);
        if (entry is null)
            return text.Key;

        var args = text.Args ?? new Dictionary<string, object>();
        return Fill(entry.Select(args), args, locale);
    }

    public string Render(string key, string locale) => Render(LocalizedText.Of(key), locale);

    /// <summary>
    /// The locale to serve, from an <c>Accept-Language</c> header. Deliberately crude - it takes
    /// the first tag whose language subtag has a catalogue and ignores the q-values - because the
    /// catalogue holds two languages and a full RFC 4647 negotiation would be machinery for a
    /// choice between <c>es</c> and <c>en</c>.
    /// </summary>
    public string Negotiate(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage))
            return DefaultLocale;

        foreach (var tag in acceptLanguage.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var language = tag.Split(';')[0].Split('-')[0].Trim();
            if (_byLocale.ContainsKey(language))
                return _byLocale.Keys.First(l => string.Equals(l, language, StringComparison.OrdinalIgnoreCase));
        }

        return DefaultLocale;
    }

    // ------------------------------------------------------------------------

    private CatalogEntry? Lookup(string key, string locale)
    {
        if (_byLocale.TryGetValue(locale, out var entries) && entries.TryGetValue(key, out var entry))
            return entry;

        return _byLocale[DefaultLocale].TryGetValue(key, out var fallback) ? fallback : null;
    }

    /// <summary>
    /// Replaces <c>{name}</c> and <c>{name:format}</c>. An unknown name is left standing rather
    /// than blanked: <c>{node}</c> in the middle of a sentence is a bug somebody reports, an empty
    /// gap is a sentence that reads fine and says the wrong thing.
    /// </summary>
    /// <summary>
    /// One argument, rendered. An argument that is itself a <see cref="LocalizedText"/> is resolved
    /// first: a challenge that fails because of a finding shows the finding's own sentence inside
    /// its own, and that sentence has to be picked in the same language rather than pasted in from
    /// whatever the producer happened to render (V7 D1.1).
    /// </summary>
    private string Value(object value, string? format, string locale) => value switch
    {
        LocalizedText nested => Render(nested, locale),
        _ => TextFormats.Apply(value, format, locale)
    };

    private string Fill(string template, IReadOnlyDictionary<string, object> args, string locale)
    {
        var result = new StringBuilder(template.Length);
        var index = 0;

        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);
            if (open < 0)
            {
                result.Append(template, index, template.Length - index);
                break;
            }

            var close = template.IndexOf('}', open);
            if (close < 0)
            {
                result.Append(template, index, template.Length - index);
                break;
            }

            result.Append(template, index, open - index);

            var placeholder = template[(open + 1)..close];
            var separator = placeholder.IndexOf(':');
            var name = separator < 0 ? placeholder : placeholder[..separator];
            var format = separator < 0 ? null : placeholder[(separator + 1)..];

            result.Append(args.TryGetValue(name, out var value)
                ? Value(value, format, locale)
                : template[open..(close + 1)]);

            index = close + 1;
        }

        return result.ToString();
    }

    private static IReadOnlyDictionary<string, CatalogEntry> Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException($"Embedded resource '{resource}' not found.");

        using var document = JsonDocument.Parse(stream);
        var entries = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);

        foreach (var property in document.RootElement.EnumerateObject())
        {
            entries[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => new CatalogEntry(property.Value.GetString()!, null),

                // A plural entry. The form is chosen by the `count` argument unless the entry
                // names another one: RETRY_AMPLIFICATION counts retries and MISSING_CACHE counts
                // consumers, and a template reading `{count}` where the sentence is about retries
                // would make the translator guess what is being counted (D1.2).
                JsonValueKind.Object => new CatalogEntry(
                    property.Value.GetProperty("other").GetString()!,
                    property.Value.TryGetProperty("one", out var one) ? one.GetString() : null,
                    property.Value.TryGetProperty("plural", out var driver) ? driver.GetString()! : "count"),

                _ => throw new InvalidOperationException(
                    $"'{property.Name}' in '{resource}' is neither a sentence nor a plural entry.")
            };
        }

        return entries;
    }

    /// <param name="One">
    /// The singular form, or null when the sentence has no number in it. Spanish and English share
    /// the same two categories, so two forms cover both catalogues; a language with more of them
    /// is the moment this becomes a category name and not a boolean.
    /// </param>
    /// <param name="Driver">Which argument decides the form. <c>count</c> unless the entry says otherwise.</param>
    private sealed record CatalogEntry(string Other, string? One, string Driver = "count")
    {
        public string Select(IReadOnlyDictionary<string, object> args) =>
            One is not null && args.TryGetValue(Driver, out var count) && IsOne(count)
                ? One
                : Other;

        private static bool IsOne(object count) =>
            count is IConvertible convertible &&
            Math.Abs(convertible.ToDecimal(CultureInfo.InvariantCulture) - 1m) < 0.0001m;
    }
}
