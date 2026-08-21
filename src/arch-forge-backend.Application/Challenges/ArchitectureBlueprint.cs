using System.Text.Json;
using System.Text.Json.Serialization;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Challenges;

/// <summary>
/// A hand-authored architecture: the starter of a challenge and its reference solution.
///
/// <strong>Nodes are wired by name, not by id.</strong> Instantiating one mints fresh
/// <see cref="Guid"/>s, so an id written in a file would refer to nothing the moment two people
/// start the same challenge - and it would be a worse thing to hand-write. Names are already the
/// contract of a starter, because <see cref="ScenarioFault"/> points that way too.
/// </summary>
public sealed class ArchitectureBlueprint
{
    public string Name { get; set; } = string.Empty;
    public List<BlueprintNode> Nodes { get; set; } = [];
    public List<BlueprintConnection> Connections { get; set; } = [];

    /// <summary>
    /// Turns the blueprint into a real architecture. Same shape a <c>POST /architectures</c>
    /// produces, so what comes back is an ordinary architecture with an ordinary share token -
    /// an attempt is not a special kind of thing (D2.2).
    /// </summary>
    public Architecture Instantiate(string name)
    {
        var now = DateTime.UtcNow;
        var architecture = new Architecture
        {
            Id = Guid.NewGuid(),
            Name = name,
            ShareToken = Guid.NewGuid().ToString("N")[..10],
            EditToken = Guid.NewGuid().ToString("N"),
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        var idsByName = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var layout = BlueprintLayout.Of(this);

        foreach (var node in Nodes)
        {
            var id = Guid.NewGuid();
            idsByName[node.Name] = id;

            var (x, y) = layout[node.Name];

            architecture.Nodes.Add(new Node
            {
                Id = id,
                ArchitectureId = architecture.Id,
                Type = node.Type,
                Name = node.Name,
                PositionX = x,
                PositionY = y,
                Configuration = Raw(node.Configuration)
            });
        }

        foreach (var edge in Connections)
        {
            architecture.Connections.Add(new Connection
            {
                Id = Guid.NewGuid(),
                ArchitectureId = architecture.Id,
                SourceNodeId = idsByName[edge.From],
                TargetNodeId = idsByName[edge.To],
                Protocol = edge.Protocol,
                Synchronous = edge.Synchronous,
                Configuration = Raw(edge.Configuration)
            });
        }

        return architecture;
    }

    /// <summary>Every failure a hand-written file can have, checked once at startup (D1.5).</summary>
    public IEnumerable<string> Validate()
    {
        if (Nodes.Count == 0)
            yield return "has no nodes";

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Name))
                yield return "has a node with no name";
            else if (!names.Add(node.Name))
                yield return $"repeats the node name '{node.Name}'";
        }

        foreach (var edge in Connections)
        {
            if (!names.Contains(edge.From))
                yield return $"connects from '{edge.From}', which is not a node";
            if (!names.Contains(edge.To))
                yield return $"connects to '{edge.To}', which is not a node";
        }
    }

    private static string Raw(JsonElement? configuration) =>
        configuration is { ValueKind: JsonValueKind.Object } value
            ? value.GetRawText()
            : Node.EmptyConfiguration;
}

public sealed class BlueprintNode
{
    public string Name { get; set; } = string.Empty;
    public NodeType Type { get; set; }

    /// <summary>
    /// <strong>Not a position.</strong> Where a component lands is computed by
    /// <see cref="BlueprintLayout"/> - entry point on top, one row per depth level, the database
    /// at the bottom - because which way a starter reads should not depend on whoever last edited
    /// the file. These two survive as the only thing the layout cannot work out on its own: which
    /// of two components sharing a row goes before the other, read across the axis the file was
    /// authored on (<c>y</c> first, then <c>x</c>).
    /// </summary>
    public double X { get; set; }

    /// <inheritdoc cref="X"/>
    public double Y { get; set; }

    [JsonPropertyName("configuration")]
    public JsonElement? Configuration { get; set; }
}

public sealed class BlueprintConnection
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public ProtocolType Protocol { get; set; }
    public bool Synchronous { get; set; } = true;

    [JsonPropertyName("configuration")]
    public JsonElement? Configuration { get; set; }
}
