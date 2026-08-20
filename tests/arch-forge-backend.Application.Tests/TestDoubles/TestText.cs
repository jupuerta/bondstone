using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Tests.TestDoubles;

/// <summary>
/// V7 D1.5. Renders a <see cref="LocalizedText"/> through the <strong>real</strong> catalogue, so
/// that the rule tests keep asserting on the sentence a user reads rather than on a key.
///
/// This is deliberate and is the cheapest test in the change. Asserting on keys would pass with an
/// empty catalogue: every rule would emit <c>finding.SPOF.unreachable.message</c>, every assertion
/// would be green, and the panel would show a key. Going through the catalogue means each of the
/// hundred-odd existing assertions now also checks that the key exists, that its placeholders match
/// the arguments the rule passes, and that the number comes out formatted the way it used to.
/// </summary>
public static class TestText
{
    private static readonly TextCatalog Catalog = new();

    /// <summary>For the exporter, which takes the pair rather than resolving keys one at a time.</summary>
    public static readonly Localizer Spanish = new(Catalog, "es");

    public static readonly Localizer English = new(Catalog, "en");

    /// <summary>Empty for a null text - a <c>Good</c> finding has no suggestion, and that is not a failure to render.</summary>
    public static string Es(this LocalizedText? text) => text is null ? string.Empty : Catalog.Render(text, "es");

    public static string En(this LocalizedText? text) => text is null ? string.Empty : Catalog.Render(text, "en");
}
