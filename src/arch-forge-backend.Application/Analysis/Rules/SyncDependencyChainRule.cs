using arch_forge_backend.Domain.Entities;

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

    public string PassedMessage => "No hay cadenas síncronas largas.";

    public string PassedExplanation =>
        "Se han recorrido las llamadas síncronas y ninguna encadena suficientes componentes como para que las latencias se sumen.";

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var synchronous = graph.Connections.Where(c => c.Synchronous).ToList();
        if (synchronous.Count == 0)
            return RuleResult.NotApplicable;

        // A cycle is already reported as a cycle; describing it again as a chain would be the same
        // fact under two codes (D4.5).
        var cyclic = graph.Facts.OfType<CyclicConnectionFact>().Select(f => f.ConnectionId).ToHashSet();
        var edges = synchronous.Where(c => !cyclic.Contains(c.Id)).ToList();

        var bySource = edges.ToLookup(c => c.SourceNodeId);
        var withIncoming = edges.Select(c => c.TargetNodeId).ToHashSet();

        var longest = new Dictionary<Guid, List<Connection>>();
        var findings = new List<RuleFinding>();

        // Reported once per chain, from the node that starts it, instead of once per node along
        // the way.
        foreach (var node in graph.Nodes.Where(n => !withIncoming.Contains(n.Id)))
        {
            var path = LongestFrom(node.Id);

            // Length is measured in components, not hops: API -> Orders -> Payments is a chain of
            // 3 (V1.1 §3.1).
            var components = path.Count + 1;
            if (components < RuleThresholds.SyncChainWarning)
                continue;

            // The cut at 5 is the literal example of §8.1 of the ideas document (D4.2).
            var severity = components >= RuleThresholds.SyncChainCritical ? Severity.Critical : Severity.Warning;

            findings.Add(new RuleFinding(
                Code,
                Area,
                severity,
                $"'{node.Name}' inicia una cadena de dependencias síncronas de {components} componentes.",
                "La petición no termina hasta que responde el último eslabón: las latencias se suman y basta con que uno falle para que fallen todos.",
                "Corta la cadena introduciendo una cola en el punto donde la operación deje de necesitar respuesta inmediata.",
                Interleave(node.Id, path)));
        }

        return RuleResult.Applicable(findings);

        // chain[n] = 1 + max(chain[children]), memoized: every node is resolved once, so the whole
        // walk is O(V + E). The on-path set is belt and braces - the graph is already acyclic here.
        List<Connection> LongestFrom(Guid nodeId, HashSet<Guid>? onPath = null)
        {
            if (longest.TryGetValue(nodeId, out var cached))
                return cached;

            onPath ??= [];
            if (!onPath.Add(nodeId))
                return [];

            var best = new List<Connection>();

            foreach (var edge in bySource[nodeId].OrderBy(c => c.TargetNodeId).ThenBy(c => c.Id))
            {
                var candidate = LongestFrom(edge.TargetNodeId, onPath);
                if (candidate.Count + 1 <= best.Count)
                    continue;

                best = candidate.Prepend(edge).ToList();
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
