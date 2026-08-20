using System.Globalization;

namespace arch_forge_backend.Application.Localization;

/// <summary>
/// V7 D1.3. How a raw argument becomes the piece of text that goes inside a sentence.
///
/// These exist because the alternative is formatting numbers before they travel, and a number
/// formatted in Spanish inside an English sentence is the failure mode that makes half-done
/// localization read worse than none. The catalogue's placeholder says which of these to apply
/// - <c>{callMs:duration}</c> - so the same argument reads <em>8 s</em> here and <em>8s</em> in a
/// language that writes it that way, with the rules changing in the catalogue and not in a rule.
///
/// <strong>The frontend implements the same four.</strong> They are part of the contract, in the
/// sense that a placeholder the other side cannot render is a sentence with a hole in it, and
/// <c>LocalizationContractTests</c> is what keeps the two lists the same length.
/// </summary>
public static class TextFormats
{
    /// <summary>A whole number: <c>1200</c>.</summary>
    public const string Number = "number";

    /// <summary>A magnitude that is allowed decimals: <c>1,25</c>. Metrics use it, counts do not.</summary>
    public const string Amount = "amount";

    /// <summary>Milliseconds, coarse: <c>8 s</c> past a second, <c>852 ms</c> below it.</summary>
    public const string Duration = "duration";

    /// <summary>Milliseconds, keeping its decimals: <c>1,5 s</c>.</summary>
    public const string DurationPrecise = "durationPrecise";

    /// <summary>A fraction as a whole percentage: <c>0.43</c> reads <c>43 %</c>.</summary>
    public const string Percent = "percent";

    /// <summary>The same, with one decimal: <c>0.435</c> reads <c>43,5 %</c>.</summary>
    public const string PercentPrecise = "percentPrecise";

    public static bool IsKnown(string? format) =>
        format is null or Number or Amount or Duration or DurationPrecise or Percent or PercentPrecise;

    public static string Apply(object value, string? format, string locale) => format switch
    {
        Number => Integer(value, locale),
        Amount => AsDecimal(value).ToString("0.##", Culture(locale)),
        Duration => AsDuration(value, locale),
        DurationPrecise => AsDurationPrecise(value, locale),
        Percent => AsPercent(value, "0", locale),
        PercentPrecise => AsPercent(value, "0.#", locale),
        _ => value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };

    /// <summary>
    /// Rounded, with no thousands separator - which is what every one of these sentences did
    /// before the texts moved out of the code, and keeping it means the Spanish document coming
    /// out of the exporter is byte-identical across the change.
    /// </summary>
    private static string Integer(object value, string locale) =>
        AsDecimal(value).ToString("0", Culture(locale));

    /// <summary>
    /// Whole seconds once past a second. The model's whole point is that healthy lives in tens of
    /// milliseconds and broken jumps to seconds, and <em>8 s</em> carries that better than
    /// <em>8 052 ms</em> (V3 plan técnico §7).
    /// </summary>
    private static string AsDuration(object value, string locale)
    {
        var ms = (int)AsDecimal(value);
        return ms >= 1_000
            ? $"{(ms / 1_000).ToString("0", Culture(locale))} s"
            : $"{ms.ToString("0", Culture(locale))} ms";
    }

    /// <summary>
    /// The same magnitude with its decimals kept. <c>TIMEOUT_TOO_SHORT</c> uses this one and the
    /// simulator uses the coarse one, and that is not an inconsistency: the finding's whole
    /// sentence is a comparison of two durations, and rounding a timeout of 1 500 ms down to
    /// <em>1 s</em> would make it argue against a number the user never set.
    /// </summary>
    private static string AsDurationPrecise(object value, string locale)
    {
        var ms = (int)AsDecimal(value);
        return ms >= 1_000
            ? $"{(ms / 1_000m).ToString("0.##", Culture(locale))} s"
            : $"{ms.ToString("0", Culture(locale))} ms";
    }

    /// <summary>
    /// Takes the fraction, not the percentage: <c>1.5</c> reads <em>150 %</em>. The space before
    /// the sign is Spanish typography and English does not use it, which is exactly the kind of
    /// detail that has to live in a formatter rather than be baked into a template.
    /// </summary>
    private static string AsPercent(object value, string pattern, string locale)
    {
        var percent = (AsDecimal(value) * 100m).ToString(pattern, Culture(locale));
        return locale.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? $"{percent}%" : $"{percent} %";
    }

    private static decimal AsDecimal(object value) => value is IConvertible convertible
        ? convertible.ToDecimal(CultureInfo.InvariantCulture)
        : 0m;

    private static CultureInfo Culture(string locale)
    {
        try
        {
            return CultureInfo.GetCultureInfo(locale);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
