using arch_forge_backend.Application.Localization;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// QA §4. <strong>The mirror of <see cref="MissingCacheRule"/></strong>: that one asks for a cache in
/// front of the database, this one notices a cache in front of nothing. The sentence already lived
/// in the catalogue, as SPOF's advice for a shared cache - <em>"una caché de la que no se puede
/// prescindir ha dejado de ser una caché"</em> - and it deserved a rule of its own for the case
/// where it is most literally true: the cache is the only place the data exists.
///
/// <strong>What makes a cache a cache is that something behind it is the truth.</strong> The
/// question is therefore not "is there a database on the canvas" but "does this cache's data have
/// an origin": whether anything that uses the cache can reach a system of record
/// (<see cref="NodeRoles.IsSystemOfRecord"/>) - through the cache, as in <c>Api → Cache → Db</c>,
/// or beside it, as in cache-aside; directly, or through the worker that warms it. A cache none of
/// whose users can reach one is holding state that nothing promises to keep: an eviction, a restart
/// or a cold node and it is gone, with no place to rebuild it from.
///
/// It lives in Resilience next to <see cref="QueueNoDlqRule"/> because it is the same failure
/// mode - losing in silence - and not in Availability: the cache going down is SPOF's question;
/// the data never coming back is this one's. A Warning, because a cache that holds only what can
/// be lost (sessions, counters, rate-limit windows) is a legitimate design the analyzer cannot tell
/// apart from the mistake, so the card asks rather than asserts.
/// </summary>
public class CacheAsStoreRule : IArchitectureRule
{
    public string Code => RuleCodes.CacheAsStore;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.CACHE_AS_STORE.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.CACHE_AS_STORE.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        // A cache nobody uses is a leftover box, and ORPHAN_NODE's (QA B4). A cache only fed by
        // the outside is not a design this rule can read.
        var caches = graph.Nodes
            .Where(n => n.Type == NodeType.Cache)
            .Where(n => graph.IncomingByTarget[n.Id].Any(edge => !NodeRoles.IsRequestFromOutside(graph, edge)))
            .ToList();

        if (caches.Count == 0)
            return RuleResult.NotApplicable;

        var findings = caches
            .Where(cache => !HasAnOrigin(graph, cache))
            .OrderBy(cache => cache.Id)
            .Select(cache =>
            {
                // The users of the cache travel with the finding: they are where the missing
                // origin would hang from, and what the canvas should light up (same as
                // MISSING_CACHE lists the consumers it wants behind a cache).
                List<FindingElement> elements = [FindingElement.Node(cache.Id)];
                elements.AddRange(graph.IncomingByTarget[cache.Id]
                    .Where(edge => !NodeRoles.IsRequestFromOutside(graph, edge))
                    .Select(edge => FindingElement.Connection(edge.Id)));

                // D5.6: no fix. Which system of record, and whether the data was meant to survive
                // at all, is the decision - the card asks it; a button would answer it.
                return new RuleFinding(
                    Code,
                    Area,
                    DefaultSeverity,
                    LocalizedText.Of("finding.CACHE_AS_STORE.noOrigin.message", ("node", cache.Name)),
                    LocalizedText.Of("finding.CACHE_AS_STORE.noOrigin.explanation", ("node", cache.Name)),
                    LocalizedText.Of("finding.CACHE_AS_STORE.noOrigin.suggestion"),
                    elements);
            })
            .ToList();

        return RuleResult.Applicable(findings);
    }

    /// <summary>
    /// Whether any user of the cache can reach a system of record - by any path, the cache
    /// included. Every edge counts, synchronous or not: a queue on the way to the database is
    /// write-behind, which is an origin arriving late rather than no origin.
    /// </summary>
    private static bool HasAnOrigin(ArchitectureGraph graph, Node cache)
    {
        var visited = new HashSet<Guid>();

        return graph.IncomingByTarget[cache.Id]
            .Where(edge => !NodeRoles.IsRequestFromOutside(graph, edge))
            .Select(edge => edge.SourceNodeId)
            .Any(userId => ReachesSystemOfRecord(graph, userId, visited));
    }

    private static bool ReachesSystemOfRecord(ArchitectureGraph graph, Guid fromId, HashSet<Guid> visited)
    {
        if (!visited.Add(fromId))
            return false;

        if (!graph.NodesById.TryGetValue(fromId, out var node))
            return false;

        if (NodeRoles.IsSystemOfRecord(node.Type))
            return true;

        return graph.OutgoingBySource[fromId]
            .OrderBy(edge => edge.TargetNodeId)
            .Any(edge => ReachesSystemOfRecord(graph, edge.TargetNodeId, visited));
    }
}
