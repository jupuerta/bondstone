namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// Two fixed phases (V2 D5.2): every producer runs before every consumer. Order inside a phase is
/// not guaranteed and must not matter - no rule reads facts published by a rule of its own phase.
/// </summary>
public enum RulePhase
{
    Producer,
    Consumer
}
