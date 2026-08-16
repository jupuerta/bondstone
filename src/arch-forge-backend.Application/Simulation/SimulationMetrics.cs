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
/// <param name="RequestsAffected">
/// V4 DT.7. The promise of §9 - <c>73% of requests affected</c> - paid two versions late, and
/// better than it was written: it lives next to <see cref="BlastRadius"/> rather than replacing it,
/// and the pair is the best summary of the product there has been. On <c>resilient-async</c> with
/// the database down they read <strong>43 % and 0 %</strong> at the same time: half the components
/// are tinted and the user notices nothing.
///
/// A <strong>bound</strong>, not a probability: the minimum served fraction along the call tree, so
/// it can be redone by hand and it names a culprit. Optimistic on purpose - two components at 50 %
/// in series give 50 %, not 25 % (plan técnico §5.4).
/// </param>
/// <param name="Bottleneck">Whatever set that minimum, so the panel can point at one component instead of tinting eight.</param>
/// <param name="BacklogRps">DT.7. What piles up in the queues because their consumers cannot keep up. A rate needs no clock.</param>
/// <param name="LoadAmplification">
/// Requests arriving at the busiest component for each one coming in the front door.
/// <strong>Not</strong> <see cref="RequestAmplification"/>, which is the worst path under a
/// scenario: on <c>worst-case</c> they read ×12 and ×24 and both are true, which is exactly why
/// they do not share a name.
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
    decimal RequestAmplification,
    decimal RequestsAffected,
    Analysis.FindingElement? Bottleneck,
    decimal BacklogRps,
    decimal LoadAmplification);
