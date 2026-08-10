namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// The six areas of the score. Declaration order is contract: <c>areas</c> is always serialized in
/// it, and it breaks ties when ordering findings (V2 D2.1, rule 4).
/// </summary>
public enum Area
{
    Availability,
    Resilience,
    Coupling,
    Scalability,
    Security,
    Observability
}
