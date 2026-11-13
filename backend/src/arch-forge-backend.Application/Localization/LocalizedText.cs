namespace bondstone_backend.Application.Localization;

public sealed record LocalizedText(string Key, IReadOnlyDictionary<string, object>? Args = null)
{
    public static LocalizedText Of(string key) => new(key);

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

        var pairs = 0;
        foreach (var pair in Args ?? new Dictionary<string, object>())
            pairs ^= HashCode.Combine(pair.Key, pair.Value);

        hash.Add(pairs);
        return hash.ToHashCode();
    }

    public static LocalizedText Of(string key, params (string Name, object Value)[] args) =>
        new(key, args.ToDictionary(arg => arg.Name, arg => arg.Value));
}
