namespace bondstone_backend.Application.Challenges;

internal static class BlueprintLayout
{
    private const double RowHeight = 160;

    private const double ColumnWidth = 260;

    public static Dictionary<string, (double X, double Y)> Of(ArchitectureBlueprint blueprint)
    {
        var declared = new Dictionary<string, int>(StringComparer.Ordinal);
        var hint = new Dictionary<string, (double Across, double Along)>(StringComparer.Ordinal);

        foreach (var node in blueprint.Nodes)
        {
            declared.TryAdd(node.Name, declared.Count);
            hint[node.Name] = (node.Y, node.X);
        }

        var callers = declared.Keys.ToDictionary(name => name, _ => new List<string>(), StringComparer.Ordinal);
        var callees = declared.Keys.ToDictionary(name => name, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var edge in blueprint.Connections)
        {
            if (edge.From == edge.To) continue;
            if (!callees.ContainsKey(edge.From) || !callers.ContainsKey(edge.To)) continue;
            if (callees[edge.From].Contains(edge.To)) continue;

            callees[edge.From].Add(edge.To);
            callers[edge.To].Add(edge.From);
        }

        var level = LevelsOf(declared, callers, callees);
        var position = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);

        foreach (var row in level.GroupBy(entry => entry.Value).OrderBy(row => row.Key))
        {
            var placed = row.Select(entry => entry.Key)
                .OrderBy(name => Barycentre(name, callers, position))
                .ThenBy(name => hint[name].Across)
                .ThenBy(name => hint[name].Along)
                .ThenBy(name => declared[name])
                .ToList();

            for (var column = 0; column < placed.Count; column++)
                position[placed[column]] = ((column - (placed.Count - 1) / 2.0) * ColumnWidth, row.Key * RowHeight);
        }

        return position;
    }

    private static Dictionary<string, int> LevelsOf(
        Dictionary<string, int> declared,
        Dictionary<string, List<string>> callers,
        Dictionary<string, List<string>> callees)
    {
        var level = declared.Keys.ToDictionary(name => name, _ => 0, StringComparer.Ordinal);
        var waiting = declared.Keys.ToDictionary(name => name, name => callers[name].Count, StringComparer.Ordinal);
        var pending = new HashSet<string>(declared.Keys, StringComparer.Ordinal);

        while (pending.Count > 0)
        {
            var ready = pending.Where(name => waiting[name] == 0).OrderBy(name => declared[name]).ToList();

            if (ready.Count == 0)
                ready =
                [
                    pending
                        .OrderByDescending(name => callees[name].Count(pending.Contains) - waiting[name])
                        .ThenBy(name => declared[name])
                        .First()
                ];

            foreach (var name in ready)
                pending.Remove(name);

            foreach (var name in ready)
            {
                foreach (var next in callees[name])
                {
                    if (!pending.Contains(next)) continue;

                    level[next] = Math.Max(level[next], level[name] + 1);
                    waiting[next]--;
                }
            }
        }

        var wired = declared.Keys.Where(name => callers[name].Count > 0 || callees[name].Count > 0).ToList();
        if (wired.Count == 0) return level;

        var floor = wired.Max(name => level[name]) + 1;
        foreach (var name in declared.Keys.Where(name => callers[name].Count == 0 && callees[name].Count == 0))
            level[name] = floor;

        return level;
    }

    private static double Barycentre(
        string name,
        Dictionary<string, List<string>> callers,
        Dictionary<string, (double X, double Y)> position)
    {
        var xs = callers[name].Where(position.ContainsKey).Select(caller => position[caller].X).ToList();

        return xs.Count == 0 ? double.MaxValue : xs.Average();
    }
}
