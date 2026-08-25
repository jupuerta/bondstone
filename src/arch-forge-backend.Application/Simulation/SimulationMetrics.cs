namespace arch_forge_backend.Application.Simulation;

public sealed record SimulationMetrics(
    int ComponentsDown,
    int ComponentsDegraded,
    int ComponentsTotal,
    decimal BlastRadius,
    int EntryPointsAffected,
    int EntryPointsTotal,
    int NominalLatencyMs,
    int? FaultedLatencyMs,
    decimal RequestAmplification,
    decimal RequestsAffected,
    Analysis.FindingElement? Bottleneck,
    decimal BacklogRps,
    decimal LoadAmplification);
