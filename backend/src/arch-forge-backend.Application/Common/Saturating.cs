namespace bondstone_backend.Application.Common;

// Every number a person types into an inspector arrives as `int?` and is believed. `instances` has
// no contract ceiling on purpose — QA V22, observation 7: the analysis already punishes the absurd
// (`OVERPROVISIONED` turns critical above ×4 of what the load asks, so 999 999 copies cost 15
// points), and a ceiling added later would refuse to save rows that are already stored above it.
//
// What that decision did not cover is what the engine does with such a number. Multiplying a cost
// per instance by a count near `int.MaxValue` wraps silently — C# arithmetic is unchecked by
// default — and the negative that comes out is then fed to `Enumerable.Sum`, which is **not**
// unchecked: it throws `OverflowException`, the handler turns that into a 500, and because the row
// is already saved every later `/analyze` on it answers 500 for ever.
//
// So the arithmetic saturates instead. Doing the work in `long` and landing on the int ceiling
// keeps every value any honest architecture produces exactly as it was — 999 999 copies of an API
// still cost 3 999 996 — and turns the one input that used to crash into a number that is merely
// very large.
public static class Saturating
{
    public static int ToInt(long value) => value switch
    {
        > int.MaxValue => int.MaxValue,
        < int.MinValue => int.MinValue,
        _ => (int)value
    };

    // The callers sum at most 400 values, each at most `int.MaxValue`, so `long` holds the running
    // total with seven orders of magnitude to spare: this clamps once, at the end, and never
    // mid-way — which would give a different answer if a later value were negative.
    public static int Sum(IEnumerable<int> values)
    {
        var total = 0L;

        foreach (var value in values)
            total += value;

        return ToInt(total);
    }

    // A product cannot wait until the end: four factors of a billion already leave `long` behind.
    // It clamps as it goes, which is only equivalent to clamping at the end while every factor is
    // 1 or more — true of its one caller, where each factor is a retry count plus one.
    public static int Product(IEnumerable<int> values)
    {
        var product = 1L;

        foreach (var value in values)
        {
            product *= value;

            if (product > int.MaxValue)
                return int.MaxValue;
        }

        return (int)product;
    }
}
