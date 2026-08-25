using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Simulation;

public sealed record ElementState(
    ElementType Type,
    Guid Id,
    HealthState State,
    LocalizedText Reason,
    string? Mechanism,
    FindingElement? CausedBy,
    int PropagationStep,
    decimal? Utilization = null);
