using System.Globalization;
using bondstone_backend.Application.Localization;

namespace bondstone_backend.Application.Tests.TestDoubles;

public static class TestText
{
    public static string Describe(this LocalizedText? text)
    {
        if (text is null)
            return "";

        if (text.Args is null || text.Args.Count == 0)
            return text.Key;

        var args = text.Args
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .Select(a => $"{a.Key}={Value(a.Value)}");

        return $"{text.Key}({string.Join(", ", args)})";
    }

    public static string Key(this LocalizedText? text) => text?.Key ?? "";

    public static string? Arg(this LocalizedText? text, string name) =>
        text?.Args is not null && text.Args.TryGetValue(name, out var value) ? Value(value) : null;

    private static string Value(object value) => value is LocalizedText nested
        ? Describe(nested)
        : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
}
