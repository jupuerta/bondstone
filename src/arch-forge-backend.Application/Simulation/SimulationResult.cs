namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// V3 D2.2 and D2.3. A contract of its own, independent of <see cref="Analysis.AnalysisResult"/>;
/// the only thing they share is <see cref="Analysis.ElementType"/> and
/// <see cref="Analysis.FindingElement"/> as a way of pointing at an element of the graph.
///
/// Reusing findings was tempting - you would inherit the panel and the highlighting for free - but
/// it mixes two modalities that are not the same: "this is always wrong" and "this is what would
/// happen if…". A panel that shows them together makes the user work out, line by line, which of
/// the two they are reading.
///
/// What <em>is</em> reused, and it is not little, is the shape: one flat list of elements the canvas
/// lights up by iterating, without knowing who produced them (V2 D2.2). There it meant an eighth
/// rule did not touch the canvas; here it means a new mechanism does not touch it either.
/// </summary>
/// <param name="States">
/// One entry per element of the graph, ordered by <c>(PropagationStep, Type, Id)</c> - so the list
/// already reads as the order of propagation and the animation only has to replay it (D5.6).
/// </param>
/// <param name="CriticalPath">
/// V17 D1. Where the latency goes: the reconstructed path from the most expensive entry point,
/// one <see cref="LatencyHop"/> per step, ending on the figure
/// <see cref="SimulationMetrics.FaultedLatencyMs"/> reports.
///
/// <strong>The scenario's path, which with no scenario is the nominal one.</strong> It is measured
/// on the same walk as <c>FaultedLatencyMs</c> and over the same entry points - the live ones, so a
/// door with a fault injected on it is not a door any more - and when nothing is broken that walk
/// and the nominal one agree digit for digit, which is exactly the case
/// <c>latency-budget</c> is graded on. Publishing one path rather than two is the decision: the two
/// coincide whenever there is nothing to compare, and where they differ, the one worth drawing is
/// the one the headline number came from.
///
/// Empty when there is no entry point left standing, the same condition that makes
/// <see cref="SimulationMetrics.FaultedLatencyMs"/> null. Not a cost of zero - there is no path.
/// </param>
/// <param name="DiscardedFaults">
/// Faults pointing at elements that are no longer in the graph. A link shared before the
/// architecture was edited must still open (D3.6), so they are dropped rather than rejected - and
/// announced, because an experiment that quietly lost half its faults is worse than one that fails
/// to load.
/// </param>
public sealed record SimulationResult(
    string SimulationVersion,
    IReadOnlyList<ElementState> States,
    SimulationMetrics Metrics,
    IReadOnlyList<Fault> DiscardedFaults,
    IReadOnlyList<LatencyHop>? CriticalPath = null)
{
    /// <summary>V17 D1. Empty rather than null when there is no path to measure.</summary>
    public IReadOnlyList<LatencyHop> CriticalPath { get; init; } = CriticalPath ?? [];

    /// <summary>
    /// Same reasoning as V2's <c>scoringVersion</c> (D1.5): it costs one field and it saves being
    /// left with no way to interpret a result the day one is stored or compared.
    /// </summary>
    /// <summary>
    /// <strong>v3 stays v3 for V17 D1.</strong> <see cref="CriticalPath"/> widens the payload
    /// without moving a millisecond: it is the argument of a <c>Math.Max</c> the walk already took,
    /// so every figure it contains was computed before it existed and every figure the simulator
    /// reports is the one it reported yesterday. A version says what the model <em>decided</em>;
    /// bumping it here would tell a stored result its numbers had aged when nothing about them
    /// moved - the same reading V16 made of <c>scoringVersion</c> when C5 and C7a widened the
    /// analysis payload.
    /// </summary>
    public const string CurrentSimulationVersion = "v3";
}
