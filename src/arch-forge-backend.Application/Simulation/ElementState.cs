using arch_forge_backend.Application.Analysis;

namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// V3 D2.2. What happened to one element of the graph, healthy ones included: §10.2 of the ideas
/// document paints <c>Orders 🟢</c> and <c>API 🟢</c> explicitly, and a green that does not travel
/// in the contract cannot be painted. It also turns the canvas's job into a lookup instead of a
/// diff against the graph.
/// </summary>
/// <param name="Reason">What happened, in words. This is what the user reads when they click a red component.</param>
/// <param name="Mechanism">
/// The name of the mechanism that <em>contained</em> - <c>fallback</c>, <c>queue</c>,
/// <c>healthCheck</c> - or null. It is what lets the UI draw "Fallback activated" without deducing
/// anything, and it is the most valuable pixel in V3: the learning does not happen in the red,
/// which only says something broke, but at the point where somebody decided it would stop breaking.
/// </param>
/// <param name="CausedBy">
/// The cause, as a drawable element. Following it backwards reconstructs the propagation path all
/// the way to the injected fault, and the chain always ends: every link sits at a strictly lower
/// <paramref name="PropagationStep"/> than the one two hops before it, even over a graph with cycles.
/// </param>
/// <param name="PropagationStep">
/// The round in which it reached its final state (D1.4). There is no time in the model; this is a
/// counter the fixed point already had, and replaying it in order is the animation of D6.3.
/// </param>
/// <param name="Utilization">
/// V4 DT.5. How full the component is - <c>1.5</c> is 150 % of its capacity - or null when it is an
/// edge, or when nobody declared any traffic.
///
/// It travels because the number is the lesson: amber says "something is off", <c>600 %</c> says
/// what and by how much, and after adding a cache it says <c>120 %</c> without the user having to
/// read a panel to find out whether anything improved. Null rather than 0 on purpose - an
/// architecture with no declared load has no utilisation, which is a different statement from
/// being idle.
/// </param>
public sealed record ElementState(
    ElementType Type,
    Guid Id,
    HealthState State,
    string Reason,
    string? Mechanism,
    FindingElement? CausedBy,
    int PropagationStep,
    decimal? Utilization = null);
