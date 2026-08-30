using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Common.Interfaces;
using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Architectures.Queries;

public class MapArchitectureFragilityQuery
{
    public string ShareToken { get; set; } = string.Empty;
}

public sealed record FragilityRow(
    Guid NodeId,
    int ComponentsDown,
    int ComponentsDegraded,
    decimal BlastRadius,
    int EntryPointsAffected,
    decimal RequestsAffected,
    int? FaultedLatencyMs,
    IReadOnlyList<FindingElement> Down,
    IReadOnlyList<FindingElement> Degraded);

public sealed record FragilityMap(
    int ComponentsTotal,
    int EntryPointsTotal,
    int NominalLatencyMs,
    IReadOnlyList<FragilityRow> Rows);

public class MapArchitectureFragilityQueryHandler
{
    private readonly IArchitectureRepository _repository;
    private readonly SimulationEngine _engine;

    public MapArchitectureFragilityQueryHandler(IArchitectureRepository repository, SimulationEngine engine)
    {
        _repository = repository;
        _engine = engine;
    }

    public async Task<FragilityMap?> Handle(MapArchitectureFragilityQuery query, CancellationToken cancellationToken = default)
    {
        var architecture = await _repository.GetByShareTokenAsync(query.ShareToken, cancellationToken);
        if (architecture is null)
            return null;

        var graph = new ArchitectureGraph(architecture.Nodes, architecture.Connections);

        return Sweep(graph, _engine);
    }

    public static FragilityMap Sweep(ArchitectureGraph graph, SimulationEngine engine)
    {
        var intact = engine.Run(graph, SimulationScenario.Empty);
        var rows = new List<FragilityRow>();

        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
        {
            var scenario = new SimulationScenario([new Fault(ElementType.Node, node.Id, FaultKinds.Down)]);
            var result = engine.Run(graph, scenario);
            var metrics = result.Metrics;

            rows.Add(new FragilityRow(
                node.Id,
                metrics.ComponentsDown,
                metrics.ComponentsDegraded,
                metrics.BlastRadius,
                metrics.EntryPointsAffected,
                metrics.RequestsAffected,
                metrics.FaultedLatencyMs,
                Tint(result, HealthState.Down),
                Tint(result, HealthState.Degraded)));
        }

        return new FragilityMap(
            graph.Nodes.Count,
            graph.EntryNodes.Count,
            intact.Metrics.NominalLatencyMs,
            rows.OrderByDescending(r => r.BlastRadius).ThenBy(r => r.NodeId).ToList());
    }

    private static IReadOnlyList<FindingElement> Tint(SimulationResult result, HealthState state) => result.States
        .Where(s => s.State == state)
        .Select(s => new FindingElement(s.Type, s.Id))
        .ToList();
}
