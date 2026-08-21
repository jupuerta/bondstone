namespace arch_forge_backend.Application.Challenges;

/// <summary>
/// Where a blueprint's components land on the canvas: the entry point on top, one row per depth
/// level, and whatever nothing calls - the database, the queue's workers - at the bottom.
///
/// <strong>A starter is read before it is edited.</strong> Which way it reads is not a detail of
/// the file it was written in, so the drawing is computed rather than hand-placed: fourteen
/// challenges hand-positioning their own nodes is fourteen chances for one of them to open
/// sideways, and the person who notices is looking at a graph, not at a JSON file.
///
/// Depth is the <em>longest</em> path from an entry point and not the shortest, which is the whole
/// reason a database read by both the API and a worker is drawn under the worker instead of beside
/// it: an arrow that points back up says the wrong thing about who depends on whom.
///
/// The <c>x</c> and <c>y</c> in the file survive as what they were always really doing - saying
/// which sibling goes before which (<see cref="BlueprintNode.X"/>).
/// </summary>
internal static class BlueprintLayout
{
    /// <summary>Between one depth level and the next. A node draws about 48 px tall.</summary>
    private const double RowHeight = 160;

    /// <summary>Between two components sharing a row. A node draws about 180 px wide.</summary>
    private const double ColumnWidth = 260;

    /// <summary>The position of every node in the blueprint, keyed by the name that wires it.</summary>
    public static Dictionary<string, (double X, double Y)> Of(ArchitectureBlueprint blueprint)
    {
        // Declaration order, the tie-break of last resort: two deploys of the same file must never
        // draw it differently.
        var declared = new Dictionary<string, int>(StringComparer.Ordinal);
        var hint = new Dictionary<string, (double Across, double Along)>(StringComparer.Ordinal);

        foreach (var node in blueprint.Nodes)
        {
            declared.TryAdd(node.Name, declared.Count);
            // Read across the axis it was authored on: a file drawn left to right separates
            // siblings with `y`, and this drawing separates them with `x`.
            hint[node.Name] = (node.Y, node.X);
        }

        var callers = declared.Keys.ToDictionary(name => name, _ => new List<string>(), StringComparer.Ordinal);
        var callees = declared.Keys.ToDictionary(name => name, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var edge in blueprint.Connections)
        {
            // A call to itself says nothing about depth, and the same call declared twice must not
            // count twice. Neither is legal, but the layout runs before anybody has said so.
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

            // Centred on the column the entry point stands in, so the graph opens downwards from
            // the middle instead of drifting right as it deepens.
            for (var column = 0; column < placed.Count; column++)
                position[placed[column]] = ((column - (placed.Count - 1) / 2.0) * ColumnWidth, row.Key * RowHeight);
        }

        return position;
    }

    /// <summary>
    /// The row each component belongs in: one below the deepest thing that calls it, and zero for
    /// the things nobody calls.
    /// </summary>
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

            // Nothing is ready and something is left: every survivor is called by another survivor,
            // which is a ring. It is opened at the survivor that most looks like the top of one -
            // the one calling the most and called by the fewest, which in the ring a god service
            // makes is the god service itself - and the calls back into it stop counting for
            // depth. A ring has no bottom: two arrows drawn pointing back up at the component
            // everything returns to is a truer picture than refusing to draw the ring at all, and
            // is the very thing that challenge is about.
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

        // A component nobody calls and that calls nothing - an Observability box standing beside
        // the graph rather than in it - is not an entry point, so it does not belong in the row
        // that means "this is where traffic arrives". It goes under the drawing.
        var wired = declared.Keys.Where(name => callers[name].Count > 0 || callees[name].Count > 0).ToList();
        if (wired.Count == 0) return level;

        var floor = wired.Max(name => level[name]) + 1;
        foreach (var name in declared.Keys.Where(name => callers[name].Count == 0 && callees[name].Count == 0))
            level[name] = floor;

        return level;
    }

    /// <summary>
    /// The average of where this component's callers ended up, so it is drawn under them. Rows are
    /// placed top down, so by the time a row is ordered its callers already have an <c>x</c> -
    /// except in the top row and at the node a broken ring opened, which sort last and fall back
    /// to the order the file declares.
    /// </summary>
    private static double Barycentre(
        string name,
        Dictionary<string, List<string>> callers,
        Dictionary<string, (double X, double Y)> position)
    {
        var xs = callers[name].Where(position.ContainsKey).Select(caller => position[caller].X).ToList();

        return xs.Count == 0 ? double.MaxValue : xs.Average();
    }
}
