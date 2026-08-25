namespace arch_forge_backend.Application.Simulation;

public sealed record SimulationResult(
    string SimulationVersion,
    IReadOnlyList<ElementState> States,
    SimulationMetrics Metrics,
    IReadOnlyList<Fault> DiscardedFaults,
    IReadOnlyList<LatencyHop>? CriticalPath = null)
{
    public IReadOnlyList<LatencyHop> CriticalPath { get; init; } = CriticalPath ?? [];

    public const string CurrentSimulationVersion = "v3";
}
