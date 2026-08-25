using arch_forge_backend.Application.Analysis;

namespace arch_forge_backend.Application.Simulation;

public sealed class InjectedFaults
{
    private readonly Dictionary<FindingElement, int> _latency = [];

    public InjectedFaults(SimulationScenario scenario)
    {
        foreach (var fault in scenario.Faults)
        {
            switch (fault.Kind)
            {
                case FaultKinds.Down when fault.Target == ElementType.Node:
                    DownNodes.Add(fault.Id);
                    break;

                case FaultKinds.Down:
                    SeveredConnections.Add(fault.Id);
                    break;

                case FaultKinds.Degraded:
                    DegradedNodes.Add(fault.Id);
                    break;

                case FaultKinds.Latency:
                    _latency[fault.Element] = fault.Value ?? 0;
                    break;
            }
        }
    }

    public HashSet<Guid> DownNodes { get; } = [];
    public HashSet<Guid> DegradedNodes { get; } = [];
    public HashSet<Guid> SeveredConnections { get; } = [];

    public bool IsDown(Guid nodeId) => DownNodes.Contains(nodeId);
    public bool IsSevered(Guid connectionId) => SeveredConnections.Contains(connectionId);

    public HealthState InitialStateOf(Guid nodeId) =>
        IsDown(nodeId) ? HealthState.Down
        : DegradedNodes.Contains(nodeId) ? HealthState.Degraded
        : HealthState.Healthy;

    public bool IsInjected(FindingElement element) =>
        element.Type == ElementType.Node
            ? DownNodes.Contains(element.Id) || DegradedNodes.Contains(element.Id)
            : SeveredConnections.Contains(element.Id);

    public CostInputs AsCostInputs(IReadOnlyDictionary<Guid, HealthState>? states = null) => new(
        states ?? DownNodes.ToDictionary(id => id, _ => HealthState.Down),
        _latency,
        SeveredConnections)
    {
        DeadNodes = DownNodes
    };

    public CostInputs AsExpiryProbe() => AsCostInputs() with { ChargeFailedCalls = false };
}
