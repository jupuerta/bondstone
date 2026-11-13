using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Localization;

namespace bondstone_backend.Application.Simulation;

public sealed record ElementState(
    ElementType Type,
    Guid Id,
    HealthState State,
    LocalizedText Reason,
    string? Mechanism,
    FindingElement? CausedBy,
    int PropagationStep,
    decimal? Utilization = null);
