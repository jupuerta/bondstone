namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// V16 C5. How full one component is, published <strong>while the architecture is being drawn</strong>
/// and not only when it is simulated.
///
/// <strong>Nothing here is new</strong>: <see cref="Simulation.LoadModel"/> has computed all three
/// numbers since V4 DT.5, and until now the only reader was <c>UNDERSIZED_COMPONENT</c>, which
/// spends them on a verdict - a Warning at 80 %, a Critical over 100 % - and throws the numbers
/// away. That made the rule an announcement: a component crosses the line and a finding appears,
/// with no way to have seen it coming three edits earlier. The meter is the same measurement
/// before the verdict, which is what turns the rule into something the user watches happen.
///
/// <strong>All three travel, not just the ratio.</strong> <see cref="Utilization"/> is what colours
/// the bar; <see cref="Rps"/> over <see cref="CapacityRps"/> is the sentence that explains it -
/// <c>340 / 500 rps</c> - and the client cannot derive it from the ratio. Publishing the division
/// as well as its two sides costs one number and keeps the rounding in one place: the client never
/// re-derives a percentage that then disagrees with the one the rule graded.
///
/// <strong>Present for every component, including the ones nobody can size.</strong> A
/// <see cref="Domain.Enums.NodeType.Client"/>, a <see cref="Domain.Enums.NodeType.Cdn"/>, a
/// <see cref="Domain.Enums.NodeType.Scheduler"/> and the telemetry box carry the enormous
/// capacities <see cref="Simulation.CapacityModel"/> hands them <em>so that they never saturate</em>,
/// so their ratio is always near zero and a meter on them says nothing. What to paint is the
/// client's decision (it knows the type of every box it draws) and not a second doctrine here:
/// this record answers "how much traffic, over how much capacity", which is a measurement, and
/// <see cref="Rules.NodeRoles.IsYoursToSize"/> answers "may I be told to grow it", which
/// is advice.
/// </summary>
/// <param name="NodeId">The component, by the id the canvas already draws it with.</param>
/// <param name="Rps">
/// Requests per second arriving at it - what got in, after any rate limit at the door turned
/// traffic away (<see cref="Simulation.LoadModel.LoadOf"/>).
/// </param>
/// <param name="CapacityRps">
/// What it can take: its per-instance capacity times the instances declared on it.
/// </param>
/// <param name="Utilization">
/// <see cref="Rps"/> over <see cref="CapacityRps"/>. <c>1.5</c> is 150 % of capacity, and 0.8 is
/// where <c>UNDERSIZED_COMPONENT</c> starts to have something to say
/// (<see cref="Simulation.CapacityModel.SaturationWarning"/>). Rounded to two decimals, the same
/// as the simulator's <see cref="Simulation.ElementState.Utilization"/>, so the two axes never
/// print the same component at two different percentages.
/// </param>
public sealed record ComponentLoad(Guid NodeId, decimal Rps, decimal CapacityRps, decimal Utilization);
