using arch_forge_backend.Domain.Entities;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V2 D4.2 and D5.4. Only synchronous chains: a long asynchronous chain is not a problem, it is
/// the solution this very analyzer recommends.
///
/// The traversal is linear. V1 searched the longest simple path over every possible path, which is
/// exponential on dense graphs; excluding the edges CYCLE marked leaves a graph that is acyclic by
/// construction, and on a DAG the longest path is a memoized walk. The precedence of D4.5 and the
/// performance problem are solved by the same mechanism - they were never two jobs.
/// </summary>
public class SyncDependencyChainRule : IArchitectureRule
{
    public string Code => RuleCodes.SyncChain;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.SYNC_CHAIN.passed.message");

    public LocalizedText PassedExplanation => LocalizedText.Of("finding.SYNC_CHAIN.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        // Client edges are the requests, not hops of the chain (QA §3.1): the chain a user
        // suffers starts at the first component that is yours, and counting the outside as a link
        // would hand every Client-fronted two-tier diagram a warning for existing.
        // V14 B9. And instrumentation is not a link either. `Obs → Api → Svc → Db` was read as a
        // chain of four whose first hop is a metrics scrape: the user's chain is three, and the
        // component the finding named as starting it was the telemetry box. A scrape does not wait
        // on what the Api waits on, so it cannot lengthen the path a request suffers.
        //
        // V14 B10. Nor is a provider's own hop: `Api -> Stripe -> Maps` is a chain of two from
        // where this architect stands, and what Stripe does with the call is Stripe's chain.
        var synchronous = graph.Connections
            .Where(c => c.Synchronous
                        && !NodeRoles.IsNotYourCall(graph, c)
                        && !NodeRoles.IsTelemetryEdge(graph, c))
            .ToList();

        if (synchronous.Count == 0)
            return RuleResult.NotApplicable;

        // A cycle is already reported as a cycle; describing it again as a chain would be the same
        // fact under two codes (D4.5).
        var cyclic = graph.Facts.OfType<CyclicConnectionFact>().Select(f => f.ConnectionId).ToHashSet();
        var edges = synchronous.Where(c => !cyclic.Contains(c.Id)).ToList();

        var bySource = edges.ToLookup(c => c.SourceNodeId);
        var withIncoming = edges.Select(c => c.TargetNodeId).ToHashSet();

        var longest = new Dictionary<Guid, (List<Connection> Path, int Logic)>();
        var findings = new List<RuleFinding>();
        var reported = new HashSet<Guid>();

        // Reported once per chain, from the node that starts it, instead of once per node along
        // the way.
        foreach (var node in graph.Nodes.Where(n => !withIncoming.Contains(n.Id)))
        {
            var (path, logicAhead) = LongestFrom(node.Id);

            // V14 B3. Length is measured in components that run logic, not in boxes on the path -
            // the layers the call crosses on its way are not links of the chain. `API -> Orders ->
            // Payments` is still the 3 the threshold was calibrated on (V1.1 §3.1); the CDN, the
            // router, the cache and the store that a realistic drawing adds around it are not.
            var components = (IsLogic(node.Id) ? 1 : 0) + logicAhead;
            if (components < RuleThresholds.SyncChainWarning)
                continue;

            // And the finding starts at the first thing it is counting. Naming the load balancer as
            // the head of a chain of services it is not one of was the same confusion, said out
            // loud - and two doors into the same chain are one finding, not one each.
            var walk = path.Select(edge => edge.TargetNodeId).Prepend(node.Id).ToList();
            var from = walk.FindIndex(IsLogic);
            var head = walk[from];

            if (!reported.Add(head))
                continue;

            path = path.Skip(from).ToList();
            var start = graph.NodesById[head];

            // The cut at 5 is the literal example of §8.1 of the ideas document (D4.2).
            var severity = components >= RuleThresholds.SyncChainCritical ? Severity.Critical : Severity.Warning;

            findings.Add(new RuleFinding(
                Code,
                Area,
                severity,
                LocalizedText.Of("finding.SYNC_CHAIN.long.message",
                    ("node", start.Name),
                    ("count", components)),
                LocalizedText.Of("finding.SYNC_CHAIN.long.explanation"),
                LocalizedText.Of(EndsInARead(path)
                    ? "finding.SYNC_CHAIN.long.suggestion.read"
                    : "finding.SYNC_CHAIN.long.suggestion"),
                Interleave(start.Id, path)));
        }

        return RuleResult.Applicable(findings);

        bool IsLogic(Guid nodeId) =>
            graph.NodesById.TryGetValue(nodeId, out var node) && NodeRoles.IsLogicComponent(node.Type);

        // V15 E1 - the half of V14 B3 that was left written down ("por el conteo, no por la
        // sugerencia"). B3 stopped counting the store at the end of the path; the advice went on
        // telling the user to cut the chain with a queue, and a chain that ends in a read cannot be
        // cut there: the request is waiting for the data, and deferring a read is not answering.
        // The last hop decides, and it reads the field MISSING_CACHE reads with the same default -
        // absent means both, and both includes a read - so the two rules stop contradicting each
        // other over one arrow, one asking for a cache in front of the read and the other for a
        // queue. Declaring the hop a write brings the queue back, which is the honest sentence for
        // a write path and the one thing the variant asks the user to say out loud.
        bool EndsInARead(IReadOnlyList<Connection> chain) =>
            chain.Count > 0
            && graph.NodesById.TryGetValue(chain[^1].TargetNodeId, out var tail)
            && NodeRoles.IsDataStore(tail.Type)
            && graph.ResilienceOf(chain[^1]).Reads;

        // chain[n] = max(chain[children]) scored in logic components, memoized: every node is
        // resolved once, so the whole walk is O(V + E). The on-path set is belt and braces - the
        // graph is already acyclic here.
        //
        // V14 B3. The longest path is the one with the most logic in it, not the one with the most
        // arrows: `Api -> Cache -> Db` and `Api -> Payments` both leave the Api by two hops, and
        // only the second one is a second link. `Logic` counts the path's targets, so the caller
        // adds itself.
        (List<Connection> Path, int Logic) LongestFrom(Guid nodeId, HashSet<Guid>? onPath = null)
        {
            if (longest.TryGetValue(nodeId, out var cached))
                return cached;

            onPath ??= [];
            if (!onPath.Add(nodeId))
                return ([], 0);

            var best = (Path: new List<Connection>(), Logic: 0);
            var found = false;

            foreach (var edge in bySource[nodeId].OrderBy(c => c.TargetNodeId).ThenBy(c => c.Id))
            {
                var candidate = LongestFrom(edge.TargetNodeId, onPath);
                var logic = candidate.Logic + (IsLogic(edge.TargetNodeId) ? 1 : 0);

                if (found && logic <= best.Logic)
                    continue;

                best = (candidate.Path.Prepend(edge).ToList(), logic);
                found = true;
            }

            onPath.Remove(nodeId);
            longest[nodeId] = best;
            return best;
        }
    }

    // node, edge, node, edge... the whole walk, so the canvas highlights it by iterating without
    // knowing which rule produced the finding (D2.2).
    private static List<FindingElement> Interleave(Guid startNodeId, IReadOnlyList<Connection> path)
    {
        var elements = new List<FindingElement> { FindingElement.Node(startNodeId) };

        foreach (var edge in path)
        {
            elements.Add(FindingElement.Connection(edge.Id));
            elements.Add(FindingElement.Node(edge.TargetNodeId));
        }

        return elements;
    }
}
