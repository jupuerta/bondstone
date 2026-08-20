using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Challenges;

/// <summary>
/// V4 D3.1. One requirement of a challenge: a <see cref="Kind"/> from the closed catalogue, the
/// parameters that kind needs, and the text the user reads.
///
/// One flat record with optional fields rather than a class per kind, for the same reason
/// <see cref="Simulation.Fault"/> carries a single nullable <c>Value</c> instead of a polymorphic
/// parameter object: the catalogue is closed, the definitions are hand-written JSON, and every
/// combination is checked when the catalogue loads. A converter hierarchy would be machinery for a
/// problem that does not exist.
/// </summary>
public sealed class ChallengeRequirement
{
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// The catalogue key of what the user reads in the checklist. Written per challenge, never
    /// generated - and a key rather than a sentence since V7 D1.1, because a challenge's prose is
    /// the one kind of text the backend never renders: no export carries it, so it lives only in
    /// the client's catalogue and the definition just points at it.
    /// </summary>
    public string LabelKey { get; set; } = string.Empty;

    // ---- Estructural -------------------------------------------------------

    public NodeType? NodeType { get; set; }
    public NodeType? FromType { get; set; }
    public NodeType? ToType { get; set; }

    /// <summary>How many, for <see cref="RequirementKinds.ContainsNode"/>. Defaults to 1.</summary>
    public int? Min { get; set; }

    /// <summary>The cap, for <see cref="RequirementKinds.MaxFanOut"/>.</summary>
    public int? Max { get; set; }

    public string? Mechanism { get; set; }

    // ---- Analítica ---------------------------------------------------------

    public string? Code { get; set; }
    public Severity? Severity { get; set; }

    // ---- Simulada ----------------------------------------------------------

    public List<ScenarioFault>? Faults { get; set; }
    public string? Metric { get; set; }
    public string? Operator { get; set; }
    public decimal? Value { get; set; }
}

/// <summary>
/// A fault inside a challenge definition, pointing at a component <strong>by name</strong>.
///
/// It cannot point by id: starting a challenge forks the starter architecture and every node gets a
/// fresh <see cref="Guid"/> (D2.3), so an id written in a definition would refer to nothing the
/// moment anyone plays. The name is the contract of the starter architecture, and the enunciado
/// already talks that way - <em>"tu servicio de pagos se cae"</em>, not <em>"a0000000-…-04 se cae"</em>.
///
/// This buys anti-cheese for free: delete <c>Payments</c> to pass "it survives Payments going down"
/// and the fault no longer resolves, so the requirement fails and says why.
/// </summary>
/// <param name="Node">The node's name, matched exactly.</param>
/// <param name="Kind">A <see cref="Simulation.FaultKinds"/> value.</param>
/// <param name="Value">Milliseconds for <c>latency</c>; null for the rest.</param>
public sealed record ScenarioFault(string Node, string Kind, int? Value = null);
