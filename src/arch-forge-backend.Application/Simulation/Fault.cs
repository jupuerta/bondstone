using arch_forge_backend.Application.Analysis;

namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// V3 D2.1. One fault pointed at one element of the graph, symmetric to
/// <see cref="FindingElement"/> - which is, along with <see cref="ElementType"/>, the only thing
/// the simulation contract shares with the analysis one (D2.3).
///
/// A single scalar parameter rather than an object per kind: of the whole catalogue only injected
/// latency carries one, and its unit is fixed by the kind. A polymorphic parameter model would be
/// machinery for a problem that does not exist yet.
/// </summary>
/// <param name="Value">Milliseconds for <see cref="FaultKinds.Latency"/>; null for the rest.</param>
public sealed record Fault(ElementType Target, Guid Id, string Kind, int? Value = null)
{
    public FindingElement Element => new(Target, Id);
}
