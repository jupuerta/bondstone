namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// Why an area does or does not carry a number (V2 D2.1, rule 1). The three states mean different
/// things and the panel paints them differently: an area nobody evaluates is not a healthy area.
/// </summary>
public enum AreaState
{
    /// <summary>Has rules and at least one was applicable. Only these enter the global average.</summary>
    Scored,

    /// <summary>No rule evaluates it yet - Security and Observability in V2.</summary>
    NoRules,

    /// <summary>Has rules, none of them applied to this architecture.</summary>
    NotApplicable
}
