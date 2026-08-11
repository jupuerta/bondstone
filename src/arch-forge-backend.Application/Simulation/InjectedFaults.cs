using arch_forge_backend.Application.Analysis;

namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// The scenario indexed for the engine to ask questions of. Composing faults needs no precedence
/// rules: the lattice is monotone, so the resulting state is the maximum and does not depend on the
/// order they are applied in (D2.1).
/// </summary>
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

    /// <summary>
    /// The state an element starts the fixed point in. <c>degraded</c> answers worse and
    /// <em>does not contain</em>: it drags the degradation upwards, which is what makes it a fault
    /// and not a mechanism (D4.1).
    /// </summary>
    public HealthState InitialStateOf(Guid nodeId) =>
        IsDown(nodeId) ? HealthState.Down
        : DegradedNodes.Contains(nodeId) ? HealthState.Degraded
        : HealthState.Healthy;

    public bool IsInjected(FindingElement element) =>
        element.Type == ElementType.Node
            ? DownNodes.Contains(element.Id) || DegradedNodes.Contains(element.Id)
            : SeveredConnections.Contains(element.Id);

    /// <summary>
    /// What the cost walk sees. Called twice: once with only the injected faults, to work out which
    /// calls end up over their timeout before any state exists, and once with the states phases A
    /// and B settled on.
    /// </summary>
    public CostInputs AsCostInputs(IReadOnlyDictionary<Guid, HealthState>? states = null) => new(
        states ?? DownNodes.ToDictionary(id => id, _ => HealthState.Down),
        _latency,
        SeveredConnections)
    {
        DeadNodes = DownNodes
    };

    /// <summary>
    /// The pass that decides which calls blow their own timeout, and nothing else. Failed calls
    /// charge nothing here, so an edge is only flagged when the <em>work</em> underneath it takes
    /// longer than its timeout - never because something below it was already broken. Without that,
    /// a fault at the bottom would blow every timeout on the way up at once and the whole cascade
    /// would land in a single round: in gateway-microservices the gateway would fall with the three
    /// services instead of one step after them, and §8.2's wave would flatten into a flash.
    /// </summary>
    public CostInputs AsExpiryProbe() => AsCostInputs() with { ChargeFailedCalls = false };
}
