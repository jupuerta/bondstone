using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Analysis.Rules;

public static class OutageImpact
{
    // V22 §11. `MISSING_RESILIENCE`, `CYCLE` and `HIGH_COUPLING` still count instead of measuring,
    // and V13 §11 left it open on the grounds that it was not clear *what* magnitude the last two
    // should report. Measured, and now it is clear: **there is not one.**
    //
    // A set overload was written and thrown away. Over the three shapes it was tried on — the cycle
    // in `worst-case`, a cycle carrying half the traffic, and a cycle whose inner calls are
    // asynchronous — the impact of losing a whole cycle came out **equal to the maximum of its
    // members**, every time. In the synchronous case each member already carries that number through
    // its own `SPOF`; in the asynchronous case the only member with a number is the one on the
    // request path, and the cycle is not what is hurting. `HIGH_COUPLING` names a single node, so its
    // number is its `SPOF` number by construction.
    //
    // Either way it repeats what another finding already says, which is what V2 D4.5 calls noise.
    // The magnitude these two have is the one they already report: how many components are in the
    // knot, and how many things hang off the hub.
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
