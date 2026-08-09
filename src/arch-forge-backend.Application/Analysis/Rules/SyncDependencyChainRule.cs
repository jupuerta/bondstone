using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Analysis.Rules;

public class SyncDependencyChainRule : IArchitectureRule
{
    // Length is measured in components, not hops: API -> Orders -> Payments is a chain of 3
    // (ArchForge_decisiones_arquitectura_V1_1.md §3.1).
    private const int MinChainComponents = 3;

    public string Code => "SYNC_CHAIN";
    public Severity Severity => Severity.Warning;

    public IEnumerable<RuleFinding> Evaluate(ArchitectureGraph graph)
    {
        var synchronousEdges = graph.Connections.Where(c => c.Synchronous).ToList();
        var edgesBySource = synchronousEdges.ToLookup(c => c.SourceNodeId);
        var nodesWithIncomingSyncEdge = synchronousEdges.Select(c => c.TargetNodeId).ToHashSet();

        foreach (var node in graph.Nodes)
        {
            // Report each chain once, from the node that starts it, instead of once per node
            // along the way.
            if (nodesWithIncomingSyncEdge.Contains(node.Id))
                continue;

            var components = FindLongestChain(node.Id, edgesBySource, [node.Id]) + 1;
            if (components < MinChainComponents)
                continue;

            yield return new RuleFinding
            {
                Code = Code,
                Severity = Severity,
                NodeId = node.Id,
                Message = $"'{node.Name}' inicia una cadena de dependencias síncronas de {components} componentes."
            };
        }
    }

    private static int FindLongestChain(Guid nodeId, ILookup<Guid, Connection> edgesBySource, HashSet<Guid> visited)
    {
        var max = 0;
        foreach (var edge in edgesBySource[nodeId])
        {
            if (!visited.Add(edge.TargetNodeId))
                continue;

            max = Math.Max(max, 1 + FindLongestChain(edge.TargetNodeId, edgesBySource, visited));
            visited.Remove(edge.TargetNodeId);
        }

        return max;
    }
}
