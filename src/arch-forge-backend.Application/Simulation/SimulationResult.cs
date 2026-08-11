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
    IReadOnlyList<Fault> DiscardedFaults)
{
    /// <summary>
    /// Same reasoning as V2's <c>scoringVersion</c> (D1.5): it costs one field and it saves being
    /// left with no way to interpret a result the day one is stored or compared.
    /// </summary>
    public const string CurrentSimulationVersion = "v3";
}
