using System.Text;
using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Export;

/// <summary>
/// V5 D2.2. The diagram as a <c>```mermaid</c> block: GitHub, GitLab and most viewers render it
/// natively, and a model reads it without trouble because it is text. It is the piece that makes
/// D1.1 true - the same file is read and seen.
///
/// <strong>Everything it emits is derived from lists that are already ordered</strong> (plan
/// técnico §6.2). The collision suffixes of <see cref="Identifiers"/> in particular are assigned
/// over the ordered list and not over arrival order: otherwise two runs on the same graph could
/// swap them and the golden files of D3.3 would flicker.
/// </summary>
public static class MermaidEmitter
{
    /// <param name="nodes">Already ordered by §6.2.</param>
    /// <param name="connections">Already ordered by §6.2.</param>
    public static string Emit(IReadOnlyList<Node> nodes, IReadOnlyList<Connection> connections)
    {
        var identifiers = Identifiers(nodes);
        var builder = new StringBuilder();

        builder.AppendLine("```mermaid");
        builder.AppendLine("flowchart LR");

        foreach (var node in nodes)
        {
            var label = MarkdownEscape.MermaidLabel($"{node.Type}: {node.Name}");
            builder.AppendLine($"  {identifiers[node.Id]}[\"{label}\"]");
        }

        if (connections.Count > 0)
            builder.AppendLine();

        foreach (var connection in connections)
        {
            // Only edges between nodes that exist are drawn. The graph is consistent today, and a
            // dangling edge should not take the whole diagram down with it.
            if (!identifiers.TryGetValue(connection.SourceNodeId, out var source) ||
                !identifiers.TryGetValue(connection.TargetNodeId, out var target))
                continue;

            // D2.2: synchronous vs asynchronous goes in the stroke and not in the label. It is the
            // distinction four of the fifteen rules and the whole simulation engine turn on, and a
            // diagram that hides it inside a word loses it at a glance.
            var arrow = connection.Synchronous ? "-->" : "-.->";

            builder.AppendLine($"  {source} {arrow}|{connection.Protocol}| {target}");
        }

        builder.Append("```");
        return builder.ToString();
    }

    /// <summary>
    /// A stable Mermaid identifier per node. Derived from the name so the text stays readable -
    /// half the value of D1.1 - with a deterministic numeric suffix on collision, because two nodes
    /// may legitimately carry the same name.
    /// </summary>
    private static Dictionary<Guid, string> Identifiers(IReadOnlyList<Node> nodes)
    {
        var identifiers = new Dictionary<Guid, string>();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            var candidate = MarkdownEscape.MermaidIdentifier(node.Name);
            var unique = candidate;
            var suffix = 1;

            while (!taken.Add(unique))
            {
                suffix++;
                unique = $"{candidate}_{suffix}";
            }

            identifiers[node.Id] = unique;
        }

        return identifiers;
    }
}
