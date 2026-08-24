namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// What a producer rule leaves behind for a consumer rule (V2 D5.1). The base record carries no
/// NodeId any more: two of the three V2 facts are about edges, not nodes.
/// </summary>
public abstract record ArchitectureFact;

/// <summary>Carries the severity because MISSING_RESILIENCE only escalates on a Critical SPOF (D4.4).</summary>
public sealed record SinglePointOfFailureFact(Guid NodeId, Severity Severity) : ArchitectureFact;

public sealed record CyclicConnectionFact(Guid ConnectionId) : ArchitectureFact;

public sealed record RetryAmplificationFact(Guid ConnectionId) : ArchitectureFact;

/// <summary>
/// QA B8. EXTERNAL_NO_FALLBACK has spoken about this call, timeout included: MISSING_RESILIENCE
/// has nothing left to add on the same edge (D4.5).
/// </summary>
public sealed record ExternalNoFallbackFact(Guid ConnectionId) : ArchitectureFact;

/// <summary>
/// V15 A2. UNDERSIZED_COMPONENT has graded this component against the declared load. Carries the
/// severity because TIMEOUT_TOO_SHORT yields only to an <em>overloaded</em> one (Critical, past
/// 100 %), where the latency CallCost computes is the outage itself and Scalability has already
/// charged it; close to the limit (Warning) the component is slower and still answering, and a
/// timeout shorter than that slower call is still TIMEOUT_TOO_SHORT's lesson.
/// </summary>
public sealed record UndersizedComponentFact(Guid NodeId, Severity Severity) : ArchitectureFact;
