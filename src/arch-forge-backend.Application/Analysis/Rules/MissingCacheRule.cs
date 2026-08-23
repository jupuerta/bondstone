using System.Text.Json;
using arch_forge_backend.Application.Configuration.Schemas;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V2 D4.3. Three conditions at once, never one: the permissive version ("any route to a database
/// without a cache") would flag <c>Api → Database</c>, the most common correct small architecture
/// there is. An analyzer that penalizes what is right loses all credibility on the first screen.
/// </summary>
public class MissingCacheRule : IArchitectureRule
{
    private static readonly JsonSerializerOptions ConfigurationOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string Code => RuleCodes.MissingCache;
    public Area Area => Area.Scalability;

    // Never escalates: it is a performance recommendation, not a failure. Treating it as critical
    // would devalue the real criticals (D4.3).
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.MISSING_CACHE.passed.message");

    public LocalizedText PassedExplanation => LocalizedText.Of("finding.MISSING_CACHE.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var databases = graph.Nodes
            .Where(node => node.Type == NodeType.Database && graph.IncomingByTarget[node.Id].Any())
            .ToList();

        if (databases.Count == 0)
            return RuleResult.NotApplicable;

        var findings = new List<RuleFinding>();

        foreach (var database in databases)
        {
            if (ReadReplicasOf(database) is > 0)
                continue;

            // Telemetry reading the database is not a consumer whose reads want a cache (QA §4,
            // coverage): it is the wire NO_OBSERVABILITY asks for, and it must not cost a point here.
            var direct = graph.IncomingByTarget[database.Id]
                .Where(c => c.Synchronous && !NodeRoles.IsTelemetryEdge(graph, c))
                .ToList();
            var effective = direct.Where(edge => IsEffectiveConsumer(graph, edge.SourceNodeId, database.Id)).ToList();

            if (effective.Count < RuleThresholds.CacheMinConsumers)
                continue;

            // Same as HIGH_COUPLING: the consumers read in the graph's order (V6 D1.1), which is
            // the one the document's connection table uses.
            List<FindingElement> elements = [FindingElement.Node(database.Id)];
            elements.AddRange(effective.Select(e => FindingElement.Connection(e.Id)));

            findings.Add(new RuleFinding(
                Code,
                Area,
                Severity.Warning,
                LocalizedText.Of("finding.MISSING_CACHE.direct.message",
                    ("node", database.Name),
                    ("count", effective.Count)),
                LocalizedText.Of("finding.MISSING_CACHE.direct.explanation"),
                LocalizedText.Of("finding.MISSING_CACHE.direct.suggestion"),
                elements)
            {
                // V4 D5.6. Two ways out and only one of them is a local graph transformation, so
                // that is the one with a button: everything that calls the database directly goes
                // through a cache instead. Read replicas stay in the suggestion as text, because
                // choosing between the two is the decision the user should be making.
                Fix = new AppliedFix(
                    FixKinds.InsertCache,
                    FindingElement.Node(database.Id),
                    LocalizedText.Of("fix.MISSING_CACHE.insert", ("node", database.Name)))
            });
        }

        return RuleResult.Applicable(findings);
    }

    /// <summary>
    /// A direct synchronous consumer that is not itself a Cache and has no Cache on its way to
    /// this database. Without this precision <c>Api → Cache → Database</c> would count the cache
    /// as a consumer of itself, and the "no cache interposed" condition would contradict itself
    /// (D4.3).
    /// </summary>
    private static bool IsEffectiveConsumer(ArchitectureGraph graph, Guid consumerId, Guid databaseId)
    {
        if (graph.NodesById.GetValueOrDefault(consumerId)?.Type == NodeType.Cache)
            return false;

        return !ReachesThroughCache(graph, consumerId, databaseId, []);
    }

    private static bool ReachesThroughCache(ArchitectureGraph graph, Guid fromId, Guid databaseId, HashSet<Guid> visited)
    {
        if (!visited.Add(fromId))
            return false;

        foreach (var edge in graph.OutgoingBySource[fromId])
        {
            var target = graph.NodesById.GetValueOrDefault(edge.TargetNodeId);
            if (target is null || target.Id == databaseId)
                continue;

            if (target.Type == NodeType.Cache && LeadsTo(graph, target.Id, databaseId, []))
                return true;

            if (ReachesThroughCache(graph, target.Id, databaseId, visited))
                return true;
        }

        return false;
    }

    private static bool LeadsTo(ArchitectureGraph graph, Guid fromId, Guid databaseId, HashSet<Guid> visited)
    {
        if (!visited.Add(fromId))
            return false;

        return graph.OutgoingBySource[fromId].Any(edge =>
            edge.TargetNodeId == databaseId || LeadsTo(graph, edge.TargetNodeId, databaseId, visited));
    }

    private static int? ReadReplicasOf(Node database)
    {
        if (string.IsNullOrWhiteSpace(database.Configuration))
            return null;

        try
        {
            return JsonSerializer
                .Deserialize<DatabaseConfiguration>(database.Configuration, ConfigurationOptions)?
                .ReadReplicas;
        }
        catch (JsonException)
        {
            // Saving validates node configuration against its schema, so this only covers rows
            // written before that validation existed. Unreadable means "not declared".
            return null;
        }
    }
}
