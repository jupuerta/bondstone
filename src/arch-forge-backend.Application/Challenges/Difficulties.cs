namespace arch_forge_backend.Application.Challenges;

/// <summary>
/// V8 §6.5. The three bands a challenge can be in, closed and validated when the catalogue loads.
///
/// It used to be a free <c>string</c>, which with four challenges cost nothing and with fourteen
/// costs the itinerary: a typo silently invented a fourth band, and <c>All</c> ordered by slug, so
/// the catalogue page read as an alphabetical list instead of a ramp. What makes this an enum
/// rather than a label is <see cref="Rank"/> - the order is the content.
/// </summary>
public static class Difficulties
{
    public const string Beginner = "beginner";
    public const string Intermediate = "intermediate";
    public const string Advanced = "advanced";

    public static bool IsKnown(string difficulty) => difficulty is Beginner or Intermediate or Advanced;

    /// <summary>Easiest first. Unknown sorts last, though the catalogue refuses to load one.</summary>
    public static int Rank(string difficulty) => difficulty switch
    {
        Beginner => 0,
        Intermediate => 1,
        Advanced => 2,
        _ => 3
    };
}
