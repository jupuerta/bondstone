using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using arch_forge_backend.Application.Localization;
using Xunit;

namespace arch_forge_backend.Application.Tests.Localization;

/// <summary>
/// V7 D1.5. The tests that make the catalogue a contract instead of a pile of strings.
///
/// Everything here checks something a compiler cannot: that the two locales carry the same keys,
/// that every placeholder names a format the renderer knows, and that a plural entry has the form
/// it claims to. The fourth check - that every key a rule emits actually exists - is bought for
/// free by <c>TestText</c>, since the rule tests render through the real catalogue and a missing
/// key comes back as the key itself.
/// </summary>
public class TextCatalogContractTests
{
    private const string Prefix = "arch_forge_backend.Application.Localization.Resources.";

    private static readonly Regex Placeholder = new(@"\{(?<name>[A-Za-z][A-Za-z0-9]*)(?::(?<format>[A-Za-z][A-Za-z0-9]*))?\}");

    [Fact]
    public void Every_locale_carries_exactly_the_same_keys()
    {
        var locales = Load();
        var reference = locales[TextCatalog.DefaultLocale].Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        foreach (var (locale, entries) in locales.Where(l => l.Key != TextCatalog.DefaultLocale))
        {
            var missing = reference.Except(entries.Keys).ToList();
            var extra = entries.Keys.Except(reference).OrderBy(k => k, StringComparer.Ordinal).ToList();

            // Named in the message rather than counted: a key that fell through is fixed by knowing
            // which one it was.
            Assert.True(missing.Count == 0, $"'{locale}' is missing: {string.Join(", ", missing)}");
            Assert.True(extra.Count == 0, $"'{locale}' has keys no other locale does: {string.Join(", ", extra)}");
        }
    }

    /// <summary>
    /// A placeholder asking for a format nobody implements renders the raw value, which is the kind
    /// of failure that reaches production looking like a small typography problem: <c>0.4285714</c>
    /// where the sentence promised <c>43 %</c>.
    /// </summary>
    [Fact]
    public void Every_placeholder_names_a_format_the_renderer_knows()
    {
        foreach (var (locale, entries) in Load())
        {
            foreach (var (key, template) in entries)
            {
                foreach (Match match in Placeholder.Matches(template))
                {
                    var format = match.Groups["format"].Success ? match.Groups["format"].Value : null;
                    Assert.True(TextFormats.IsKnown(format), $"'{key}' in '{locale}' asks for the unknown format '{format}'.");
                }
            }
        }
    }

    /// <summary>
    /// The same sentence has to take the same arguments in every language. A translator who drops
    /// <c>{node}</c> writes a grammatical sentence that no longer says which component, and nothing
    /// else in the system would notice.
    /// </summary>
    [Fact]
    public void A_key_takes_the_same_arguments_in_every_locale()
    {
        var locales = Load();
        var reference = locales[TextCatalog.DefaultLocale];

        foreach (var (locale, entries) in locales.Where(l => l.Key != TextCatalog.DefaultLocale))
        {
            foreach (var (key, template) in entries.Where(e => reference.ContainsKey(e.Key)))
            {
                Assert.True(
                    Names(template).SetEquals(Names(reference[key])),
                    $"'{key}' takes {Show(reference[key])} in '{TextCatalog.DefaultLocale}' and {Show(template)} in '{locale}'.");
            }
        }
    }

    /// <summary>
    /// A plural entry that forgets <c>other</c> is a sentence that cannot be rendered at all, and
    /// one whose plural form never mentions the number it branches on is a plural entry that did
    /// not need to be one.
    ///
    /// <strong>Only the plural form is checked.</strong> The singular is allowed to drop the
    /// number, and the good ones do: <em>"La única llamada a un Service no tiene timeout"</em>
    /// reads better than <em>"1 de las 1 llamadas…"</em>, and that freedom is half the reason for
    /// having two forms at all.
    /// </summary>
    [Fact]
    public void Every_plural_entry_has_both_forms_and_a_number_to_choose_by()
    {
        foreach (var (locale, resource) in Resources())
        {
            using var document = JsonDocument.Parse(resource);

            foreach (var property in document.RootElement.EnumerateObject()
                         .Where(p => p.Value.ValueKind == JsonValueKind.Object))
            {
                Assert.True(property.Value.TryGetProperty("other", out var other),
                    $"'{property.Name}' in '{locale}' is a plural entry with no 'other' form.");
                Assert.True(property.Value.TryGetProperty("one", out var one),
                    $"'{property.Name}' in '{locale}' is a plural entry with no 'one' form.");

                var driver = property.Value.TryGetProperty("plural", out var named) ? named.GetString()! : "count";

                Assert.True(Names(other.GetString()!).Contains(driver),
                    $"'{property.Name}' in '{locale}' picks its form by '{driver}', which its plural form never uses.");

                Assert.False(string.IsNullOrWhiteSpace(one.GetString()),
                    $"'{property.Name}' in '{locale}' has an empty singular form.");
            }
        }
    }

    /// <summary>
    /// The catalogue is what the exporter reads, so a locale that fails to parse takes the export
    /// down. Constructing it is the check.
    /// </summary>
    [Fact]
    public void The_catalogue_loads_and_knows_its_locales()
    {
        var catalog = new TextCatalog();

        Assert.Contains(TextCatalog.DefaultLocale, catalog.Locales);
        Assert.Contains("en", catalog.Locales);
    }

    [Theory]
    [InlineData(null, "es")]
    [InlineData("", "es")]
    [InlineData("en", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("en-GB,en;q=0.9", "en")]
    [InlineData("de-DE,de;q=0.9", "es")]
    [InlineData("de,en;q=0.8", "en")]
    public void Accept_language_falls_back_rather_than_failing(string? header, string expected) =>
        Assert.Equal(expected, new TextCatalog().Negotiate(header));

    /// <summary>An unknown key comes back as itself: a document with a gap beats no document.</summary>
    [Fact]
    public void An_unknown_key_renders_as_the_key()
    {
        var catalog = new TextCatalog();

        Assert.Equal("nope.not.here", catalog.Render(LocalizedText.Of("nope.not.here"), "es"));
    }

    // ------------------------------------------------------------------------

    private static HashSet<string> Names(string template) =>
        Placeholder.Matches(template).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);

    private static string Show(string template)
    {
        var names = Names(template).OrderBy(n => n, StringComparer.Ordinal).ToList();
        return names.Count == 0 ? "no arguments" : $"{{{string.Join(", ", names)}}}";
    }

    /// <summary>Every sentence of every locale, plural forms flattened into separate entries.</summary>
    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        var locales = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var (locale, resource) in Resources())
        {
            using var document = JsonDocument.Parse(resource);
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    entries[property.Name] = property.Value.GetString()!;
                    continue;
                }

                foreach (var form in new[] { "one", "other" })
                {
                    if (property.Value.TryGetProperty(form, out var value))
                        entries[$"{property.Name}#{form}"] = value.GetString()!;
                }

                // The key itself, so key parity compares keys and not forms.
                entries[property.Name] = property.Value.GetProperty("other").GetString()!;
            }

            locales[locale] = entries;
        }

        return locales;
    }

    private static IEnumerable<(string Locale, string Json)> Resources()
    {
        var assembly = typeof(TextCatalog).Assembly;

        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
                     .OrderBy(n => n, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return (name[Prefix.Length..^".json".Length], reader.ReadToEnd());
        }
    }
}
