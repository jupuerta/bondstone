using System.Text.Json;
using arch_forge_backend.Application.Analysis;

namespace arch_forge_backend.Application.Architectures;

/// <summary>
/// V16 C11. The one definition of how a snapshot's per-area breakdown is written down and read
/// back, so the command that stores it and the query that publishes it cannot disagree about the
/// shape of a column neither of them owns alone.
///
/// <strong>Only the scored areas travel.</strong> <see cref="AreaScore"/> carries a state and a
/// nullable number, and the history has no use for the difference between "no rule of this area
/// applied" and "no rule of this area exists yet": both are versions this axis says nothing about,
/// and an axis that says nothing cannot have gone up. Writing them as absent instead of as null
/// keeps the column small and leaves exactly one reading of a key that is there.
///
/// <see cref="AreaScore.Penalties"/> is deliberately dropped. It is the arithmetic behind one
/// number for the panel that is showing that number now; storing it on every save would put the
/// whole analysis in the history table to answer a question - <em>which areas went up</em> - that
/// six integers answer.
/// </summary>
public static class SnapshotAreas
{
    /// <summary>What the column holds when there is nothing to break down, and its column default.</summary>
    public const string Empty = "{}";

    /// <summary>
    /// The map serializes with the area's name as the key - <c>{"Availability": 90}</c> - and not
    /// its ordinal, which is what makes the column readable in the database and, more to the point,
    /// survives somebody inserting a seventh area into the middle of the enum. The declaration
    /// order of <see cref="Area"/> is already contract (V2 D2.1); this way the stored rows do not
    /// quietly depend on it too.
    /// </summary>
    public static string Write(IReadOnlyList<AreaScore> areas)
    {
        var scored = areas
            .Where(a => a.State == AreaState.Scored && a.Score.HasValue)
            .ToDictionary(a => a.Area, a => a.Score!.Value);

        return scored.Count == 0 ? Empty : JsonSerializer.Serialize(scored);
    }

    /// <summary>
    /// Null rather than an empty map when there is nothing stored, for the reason
    /// <c>GetArchitectureHistory.ReadSummary</c> gives: "this version scored no area" and "this row
    /// predates the column" are both claims the reader must not turn into "no area moved".
    /// </summary>
    public static IReadOnlyDictionary<Area, int>? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == Empty)
            return null;

        try
        {
            var read = JsonSerializer.Deserialize<Dictionary<Area, int>>(json);
            return read is { Count: > 0 } ? read : null;
        }
        catch (JsonException)
        {
            // A snapshot with an unreadable breakdown still has a version and a score, and those
            // are what the line is drawn from. Losing the detail beats losing the point.
            return null;
        }
    }
}
