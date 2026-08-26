using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Common.Interfaces;
using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Architectures.Queries;

public class SweepArchitectureLoadQuery
{
    public string ShareToken { get; set; } = string.Empty;
}

public sealed record LoadCurvePoint(
    decimal Scale,
    decimal EnteringRps,
    int NominalLatencyMs,
    decimal BacklogRps,
    decimal LoadAmplification,
    IReadOnlyList<ComponentLoad> Components);

public sealed record LoadBreakPoint(
    decimal Scale,
    Guid NodeId,
    decimal EnteringRps,
    decimal Rps,
    decimal Utilization);

public sealed record LoadCurve(
    IReadOnlyList<LoadCurvePoint> Points,
    LoadBreakPoint? BreakPoint);

public class SweepArchitectureLoadQueryHandler
{
    public static readonly IReadOnlyList<decimal> Ladder =
    [
        1m, 1.25m, 1.6m, 2m, 2.5m, 3.15m, 4m, 5m, 6.3m, 8m, 10m, 12.5m, 16m, 20m
    ];

    private readonly IArchitectureRepository _repository;

    public SweepArchitectureLoadQueryHandler(IArchitectureRepository repository)
    {
        _repository = repository;
    }

    public async Task<LoadCurve?> Handle(SweepArchitectureLoadQuery query, CancellationToken cancellationToken = default)
    {
        var architecture = await _repository.GetByShareTokenAsync(query.ShareToken, cancellationToken);
        if (architecture is null)
            return null;

        return Sweep(new ArchitectureGraph(architecture.Nodes, architecture.Connections));
    }

    public static LoadCurve Sweep(ArchitectureGraph graph)
    {
        var points = Ladder.Select(scale => At(graph, scale)).ToList();

        return new LoadCurve(points, FirstBreak(points));
    }

    private static LoadCurvePoint At(ArchitectureGraph graph, decimal scale)
    {
        var load = LoadModel.Of(graph, scale);
        var cost = CallCost.Of(graph, CostInputs.Nominal with { QueueingFactors = load.QueueingFactors });

        return new LoadCurvePoint(
            scale,
            Math.Round(load.EnteringRps, 2),
            cost.WorstFrom(graph.RootNodes) ?? 0,
            Math.Round(load.BacklogRps, 2),
            load.LoadAmplification,
            Components(graph, load));
    }

    private static IReadOnlyList<ComponentLoad> Components(ArchitectureGraph graph, LoadModel load)
    {
        if (load.IsIdle)
            return [];

        return graph.Nodes
            .Select(node => new ComponentLoad(
                node.Id,
                Math.Round(load.LoadOf(node.Id), 2),
                load.CapacityOf(node.Id),
                Math.Round(load.UtilizationOf(node.Id), 2)))
            .ToList();
    }

    private static LoadBreakPoint? FirstBreak(IEnumerable<LoadCurvePoint> points)
    {
        foreach (var point in points)
        {
            var worst = point.Components
                .Where(c => c.Utilization >= CapacityModel.SaturationWarning)
                .OrderByDescending(c => c.Utilization)
                .ThenBy(c => c.NodeId)
                .FirstOrDefault();

            if (worst is not null)
                return new LoadBreakPoint(point.Scale, worst.NodeId, point.EnteringRps, worst.Rps, worst.Utilization);
        }

        return null;
    }
}
