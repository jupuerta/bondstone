using System.Globalization;
using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Tests.TestDoubles;

/// <summary>
/// V7 D2.1. What a test reads when it wants to talk about a finding's text.
///
/// There is <strong>no catalogue on this side any more</strong>: the backend emits keys and the
/// client owns every word. So these assertions stopped comparing Spanish sentences and started
/// comparing what the backend actually promises — <em>which</em> sentence, and <em>what goes in
/// it</em>. That is the better test: a change of wording is no longer a change of behaviour here,
/// and a rule that quietly stops passing the node's name now fails.
///
/// The sentences themselves are checked where they are rendered: <c>markdownExporter.test.ts</c> in
/// the frontend replays the same five golden documents against recorded payloads from these rules.
/// </summary>
public static class TestText
{
    /// <summary>
    /// <c>finding.SPOF.unreachable.message(count=4, node=Db)</c> — the key and its arguments in one
    /// line, arguments sorted so the string does not depend on insertion order. Nested texts are
    /// described in place, which is how a challenge quoting a finding reads.
    /// </summary>
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

    /// <summary>One argument, as a string. Null when the text does not carry it - which is a failure worth seeing.</summary>
    public static string? Arg(this LocalizedText? text, string name) =>
        text?.Args is not null && text.Args.TryGetValue(name, out var value) ? Value(value) : null;

    private static string Value(object value) => value is LocalizedText nested
        ? Describe(nested)
        : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
}
