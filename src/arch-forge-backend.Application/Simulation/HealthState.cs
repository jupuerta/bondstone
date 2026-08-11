namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// V3 D1.3. An ordered lattice shared by nodes and edges: <c>Healthy &lt; Degraded &lt; Down</c>.
/// The order is the whole engine - propagation is a monotone fixed point that takes the maximum,
/// which is what makes it converge over graphs with cycles (D1.5) and what makes composing several
/// faults independent of the order they are applied in (D2.1).
///
/// On an edge the values describe the call: <c>Down</c> is the call that does not get through,
/// <c>Degraded</c> the one that gets through worse - retried, slower, answered by a fallback. The
/// UI labels that one "Broken"; it is presentation, not a fourth value.
///
/// <c>Degraded</c> is the state that matters: a simulator with two states does not teach
/// resilience, it paints things red.
/// </summary>
public enum HealthState
{
    Healthy,
    Degraded,
    Down
}
