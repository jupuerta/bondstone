using System.Text.Json;
using arch_forge_backend.Application.Configuration.Schemas;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

public class MissingCacheRule : IArchitectureRule
{
    private static readonly JsonSerializerOptions ConfigurationOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string Code => RuleCodes.MissingCache;
    public Area Area => Area.Scalability;

    // V20. The only rule of the analyzer that is an opportunity rather than a defect: a database
    // read hot enough to be worth a cache in front of it. Everything it suggests still works, which
    // is exactly what `Info` means — and it is the finding V13 §11 named when it asked for the level,
    // because sharing `Warning` with `QUEUE_NO_DLQ` put "this could be faster" next to "you are
    // losing messages".
    public Severity DefaultSeverity => Severity.Info;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.MISSING_CACHE.passed.message");

    public LocalizedText PassedExplanation => LocalizedText.Of("finding.MISSING_CACHE.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var databases = graph.Nodes
            .Where(node => node.Type == NodeType.Database && graph.IncomingByTarget[node.Id].Any())
            .ToList();

        if (databases.Count == 0)
            return RuleResult.NotApplicable("rule.na.MISSING_CACHE.noDatabase");

        var findings = new List<RuleFinding>();

        foreach (var database in databases)
        {
            if (ReadReplicasOf(database) is > 0)
                continue;

            // Only what a cache can take: a read, declared or undeclared. A call that also writes is
            // one arrow, and the button cannot send half of it through a cache — it used to reroute
            // the whole call and the declared writes vanished with it (QA 30-08, §10.3 point 5).
            var direct = graph.IncomingByTarget[database.Id]
                .Where(c => c.Synchronous && !NodeRoles.IsTelemetryEdge(graph, c))
                .Where(c => !graph.ResilienceOf(c).DeclaresWrite)
                .ToList();
            var effective = direct.Where(edge => IsEffectiveConsumer(graph, edge.SourceNodeId, database.Id)).ToList();

            var consumers = effective
                .Select(edge => graph.NodesById.GetValueOrDefault(edge.SourceNodeId))
                .Where(source => source is not null)
                .Select(source => NodeRoles.ReplicaSetOf(graph, source!))
                .Distinct()
                .Count();

            if (consumers < RuleThresholds.CacheMinConsumers)
                continue;

            List<FindingElement> elements = [FindingElement.Node(database.Id)];
            elements.AddRange(effective.Select(e => FindingElement.Connection(e.Id)));

            findings.Add(new RuleFinding(
                Code,
                Area,
                Severity.Info,
                LocalizedText.Of("finding.MISSING_CACHE.direct.message",
                    ("node", database.Name),
                    ("count", consumers)),
                LocalizedText.Of("finding.MISSING_CACHE.direct.explanation"),
                LocalizedText.Of("finding.MISSING_CACHE.direct.suggestion"),
                elements)
            {
                Fix = new AppliedFix(
                    FixKinds.InsertCache,
                    FindingElement.Node(database.Id),
                    LocalizedText.Of("fix.MISSING_CACHE.insert", ("node", database.Name)))
            });
        }

        return RuleResult.Applicable(findings);
    }

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
            return null;
        }
    }
}
