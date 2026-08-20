namespace arch_forge_backend.Application.Localization;

/// <summary>
/// V7 D1.1. A piece of user-facing text as <strong>data</strong>: which sentence, and the values
/// that go inside it. Nothing here is in any language.
///
/// <strong>Named arguments, never positional.</strong> <c>"'{node}' depende de {count}
/// componentes"</c> puts the subject first in Spanish and the English keeps the same order, but
/// nothing guarantees the next language does - a list of two values indexed by position would be
/// a bet on word order that breaks the first time somebody translates into German.
///
/// <strong>Numbers travel raw.</strong> <see cref="Args"/> carries <c>8052</c>, not <c>"8 s"</c>,
/// and the catalogue's placeholder says how to render it (<c>{callMs:duration}</c>). Formatting a
/// number on this side would put a Spanish thousands separator inside an English sentence, which
/// is the failure that makes half-done localization look worse than none.
///
/// Node names are user data: they travel as arguments and are never translated.
/// </summary>
/// <param name="Key">A catalogue key, e.g. <c>finding.SPOF.unreachable.message</c>.</param>
/// <param name="Args">
/// Values for the placeholders. Null - not an empty dictionary - when the sentence has none, so
/// the field disappears from the JSON instead of travelling as <c>{}</c> on most findings.
/// </param>
public sealed record LocalizedText(string Key, IReadOnlyDictionary<string, object>? Args = null)
{
    public static LocalizedText Of(string key) => new(key);

    /// <summary>
    /// Value equality over <see cref="Args"/>, which the compiler-generated one does not give: a
    /// record compares a dictionary by reference, so two texts with the same key and the same
    /// arguments would come out different.
    ///
    /// <strong>This is load-bearing, not tidiness.</strong> V6 D1.1 made "shuffling the input does
    /// not change the analysis" a tested property, and both that test and the simulator's compare
    /// whole results - which now contain these. Without this, every one of them fails for a reason
    /// that has nothing to do with ordering.
    /// </summary>
    public bool Equals(LocalizedText? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (!string.Equals(Key, other.Key, StringComparison.Ordinal))
            return false;

        if (Args is null || other.Args is null)
            return Args is null && other.Args is null;

        return Args.Count == other.Args.Count
               && Args.All(pair => other.Args.TryGetValue(pair.Key, out var value) && Equals(pair.Value, value));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Key, StringComparer.Ordinal);

        // Order-independent, because two argument bags with the same pairs in a different order are
        // the same text: XOR of the per-pair hashes rather than folding them in sequence.
        var pairs = 0;
        foreach (var pair in Args ?? new Dictionary<string, object>())
            pairs ^= HashCode.Combine(pair.Key, pair.Value);

        hash.Add(pairs);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Tuples rather than an anonymous object so the argument names survive as strings the
    /// catalogue test can read: the key/placeholder pairing is checked at build time, not at the
    /// moment a user reads a sentence with a <c>{node}</c> still in it.
    /// </summary>
    public static LocalizedText Of(string key, params (string Name, object Value)[] args) =>
        new(key, args.ToDictionary(arg => arg.Name, arg => arg.Value));
}
