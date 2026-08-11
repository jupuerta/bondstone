namespace arch_forge_backend.Application.Simulation;

using arch_forge_backend.Application.Analysis;

/// <summary>
/// V3 D4.1, the closed catalogue. Three kinds and no more: packet loss and anything probabilistic
/// left with D1.2, saturation with D3.3, and faults that recover over time with D1.4.
///
/// The kind travels as a string, like V2's rule codes (D2.4) - but for the opposite reason. There
/// the string was <em>output</em>, and a code the client did not know about could be painted with a
/// default style; here it is <em>input</em>, and an unknown kind cannot be "applied by default".
/// So it is validated against this list and anything else is a 400.
/// </summary>
public static class FaultKinds
{
    /// <summary>Node or edge. On an edge it is a partition: the target is alive, you just cannot reach it.</summary>
    public const string Down = "down";

    /// <summary>
    /// Node only. Answers worse and <em>does not contain</em>: it drags the degradation upwards.
    /// This is what replaces the <c>20% packet loss</c> that D1.2 threw out - the same teaching
    /// case without a dice roll.
    /// </summary>
    public const string Degraded = "degraded";

    /// <summary>
    /// Node or edge, in milliseconds. Changes no state by itself - but if the call ends up costing
    /// more than its timeout it expires always, and then it behaves like a dead target. That single
    /// line is the reason timeouts exist, and the simulator produces it without simulating time:
    /// it is a comparison between two numbers.
    /// </summary>
    public const string Latency = "latency";

    public static bool IsKnown(string kind) => kind is Down or Degraded or Latency;

    /// <summary>D4.1: <c>degraded</c> is about a component answering badly; an edge has nobody to answer.</summary>
    public static bool AppliesTo(string kind, ElementType target) =>
        kind is not Degraded || target == ElementType.Node;

    public static bool RequiresValue(string kind) => kind == Latency;
}
