using bondstone_backend.Domain.Enums;

namespace bondstone_backend.Application.Simulation;

public static class ModelReference
{
    private const decimal CurveStep = 0.025m;

    private const decimal CurveEnd = 2m;

    public static ModelReferenceDto Snapshot { get; } = Build();

    private static ModelReferenceDto Build() => new(
        Enum.GetValues<NodeType>()
            .Select(type => new ComponentModelDto(
                type,
                CapacityModel.PerInstance(type),
                LatencyModel.Processing(type),
                CostModel.PerInstance(type)))
            .ToList(),
        Enum.GetValues<ProtocolType>()
            .Select(protocol => new NetworkHopDto(protocol, LatencyModel.Hop(protocol)))
            .ToList(),
        CapacityModel.SaturationWarning,
        LatencyModel.UnboundedWait,
        CapacityModel.DefaultHitRatio,
        CapacityModel.DefaultCdnHitRatio,
        BuildQueueing());

    private static QueueingCurveDto BuildQueueing()
    {
        var points = new List<QueueingPointDto>();

        for (var utilization = 0m; utilization <= CurveEnd; utilization += CurveStep)
            points.Add(new QueueingPointDto(utilization, CapacityModel.QueueingFactor(utilization)));

        return new QueueingCurveDto(CapacityModel.QueueingFactor(1m), points);
    }
}

public sealed record ModelReferenceDto(
    IReadOnlyList<ComponentModelDto> Components,
    IReadOnlyList<NetworkHopDto> Network,
    decimal SaturationWarning,
    int UnboundedWaitMs,
    decimal DefaultHitRatio,
    decimal DefaultCdnHitRatio,
    QueueingCurveDto Queueing);

// The cost is the third constant of the same family as capacity and processing (costmodel V20,
// C4). It travels here rather than being copied into the frontend so /model cannot say 10 the day
// the model stops charging 10 - the same reason the queueing cap travels instead of being written
// into the formula on screen.
public sealed record ComponentModelDto(NodeType Type, int CapacityPerInstance, int ProcessingMs, int CostPerInstance);

public sealed record NetworkHopDto(ProtocolType Protocol, int Ms);

public sealed record QueueingCurveDto(decimal Cap, IReadOnlyList<QueueingPointDto> Points);

public sealed record QueueingPointDto(decimal Utilization, decimal Factor);
