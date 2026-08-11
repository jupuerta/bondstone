namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// V3 D1.6. The simulation's own figures, with their own names and <strong>none of them out of
/// 100</strong>: two numbers out of 100 on the same screen read as comparable even when they
/// measure different things. The Architecture Score of V2 does not move - it says how well the
/// thing is built, and this says what happens when something breaks. That tension is the product's
/// best argument, not a bug to hide.
/// </summary>
/// <param name="BlastRadius">Affected components over total, as a fraction. Travels computed so the panel never derives a figure that disagrees with the list.</param>
/// <param name="EntryPointsAffected">
/// Entry points that do not end up Healthy, over the total (D3.2). A degraded entry point is
/// serving worse, and calling that unaffected would be saying nothing happens to the user. This is
/// what replaces the "73% of requests affected" of §9: no edge gains a traffic weight, because a
/// weight is a number the user has to invent and every metric derived from it inherits the invention.
/// </param>
/// <param name="FaultedLatencyMs">
/// Null when no entry point is left standing - there is no path to measure, and a 0 would read as
/// "instant" (plan técnico §2). The two absolute figures travel, never the difference: in one of
/// the four fixtures the subtraction comes out negative, because failing fast is faster than working.
/// </param>
/// <param name="RequestAmplification">
/// Π(retries + 1) along the synchronous chain, computed <em>under the scenario</em>. D3.3: it is a
/// magnitude with an explanation, never a cause of a change of state. An architecture can come out
/// with everything Healthy and a ×12 next to it - that is not a contradiction, it is that
/// amplification is a risk under load and this simulator does not model load.
/// </param>
public sealed record SimulationMetrics(
    int ComponentsDown,
    int ComponentsDegraded,
    int ComponentsTotal,
    decimal BlastRadius,
    int EntryPointsAffected,
    int EntryPointsTotal,
    int NominalLatencyMs,
    int? FaultedLatencyMs,
    decimal RequestAmplification);
