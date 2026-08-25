using System.Text.Json;
using arch_forge_backend.Application.Analysis;

namespace arch_forge_backend.Application.Architectures;

public static class SnapshotAreas
{
    public const string Empty = "{}";

    public static string Write(IReadOnlyList<AreaScore> areas)
    {
        var scored = areas
            .Where(a => a.State == AreaState.Scored && a.Score.HasValue)
            .ToDictionary(a => a.Area, a => a.Score!.Value);

        return scored.Count == 0 ? Empty : JsonSerializer.Serialize(scored);
    }

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
            return null;
        }
    }
}
