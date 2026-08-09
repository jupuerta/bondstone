using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Analysis.Rules;

public class SyncDependencyChainRule : IArchitectureRule
{
    private const int MinChainLength = 3;

    public string Code => "SYNC_CHAIN";
    public Severity Severity => Severity.Warning;

    public IEnumerable<RuleFinding> Evaluate(ArchitectureGraph graph)
    {
        var edgesBySource = graph.Connections
            .Where(c => c.Synchronous)
            .ToLookup(c => c.SourceNodeId);

        foreach (var node in graph.Nodes)
        {
            var chainLength = FindLongestChain(node.Id, edgesBySource, new HashSet<Guid> { node.Id });
            if (chainLength < MinChainLength)
                continue;

            yield return new RuleFinding
            {
                Code = Code,
                Severity = Severity,
                NodeId = node.Id,
                Message = $"'{node.Name}' inicia una cadena de dependencias síncronas de longitud {chainLength}."
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
