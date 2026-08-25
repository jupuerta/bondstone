using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Analysis.Rules;

public static class OutageImpact
{
    public static decimal OfLosing(ArchitectureGraph graph, LoadModel load, Guid nodeId)
    {
        var roots = TrafficRoots.Of(graph);

        if (roots.Any(root => root.Id == nodeId))
        {
            return NodeRoles.ActsFromInside(graph.NodesById[nodeId].Type) ? 0m : 1m;
        }

        var served = RequestYield.Of(graph, load, new YieldInputs(new HashSet<Guid> { nodeId }, new HashSet<Guid>()));

        var worst = roots.Select(root => served.Of(root.Id)).DefaultIfEmpty(1m).Min();

        return Math.Round(1m - worst, 4);
    }
}
