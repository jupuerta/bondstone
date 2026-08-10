using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V2 VS1: adapted to the new contract, with severity scaled by length (D4.2) and the chain
/// interleaved into Elements (D2.2). VS3 replaces the traversal itself: excluding the edges CYCLE
/// marks leaves an acyclic synchronous graph, and on a DAG the longest path is linear instead of
/// exponential (D5.4).
/// </summary>
public class SyncDependencyChainRule : IArchitectureRule
{
    public string Code => RuleCodes.SyncChain;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public string PassedMessage => "No hay cadenas síncronas largas.";

    public string PassedExplanation =>
        "Se han recorrido las llamadas síncronas y ninguna encadena suficientes componentes como para acumular latencia y fallos.";

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var synchronousEdges = graph.Connections.Where(c => c.Synchronous).ToList();
        if (synchronousEdges.Count == 0)
            return RuleResult.NotApplicable;

        var edgesBySource = synchronousEdges.ToLookup(c => c.SourceNodeId);
        var nodesWithIncomingSyncEdge = synchronousEdges.Select(c => c.TargetNodeId).ToHashSet();

        var findings = new List<RuleFinding>();

        foreach (var node in graph.Nodes)
        {
            // Report each chain once, from the node that starts it, instead of once per node
            // along the way.
            if (nodesWithIncomingSyncEdge.Contains(node.Id))
                continue;

            var path = LongestChain(node.Id, edgesBySource, [node.Id]);

            // Length is measured in components, not hops: API -> Orders -> Payments is a chain of
            // 3 (V1.1 §3.1).
            var components = path.Count + 1;
            if (components < RuleThresholds.SyncChainWarning)
                continue;

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
    }

    // node, edge, node, edge... the whole walk, so the canvas can highlight it by iterating
    // without knowing which rule produced the finding (D2.2).
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

    private static List<Connection> LongestChain(Guid nodeId, ILookup<Guid, Connection> edgesBySource, HashSet<Guid> visited)
    {
        var longest = new List<Connection>();

        foreach (var edge in edgesBySource[nodeId])
        {
            if (!visited.Add(edge.TargetNodeId))
                continue;

            var candidate = LongestChain(edge.TargetNodeId, edgesBySource, visited);
            visited.Remove(edge.TargetNodeId);

            if (candidate.Count + 1 <= longest.Count)
                continue;

            candidate.Insert(0, edge);
            longest = candidate;
        }

        return longest;
    }
}
