using arch_forge_backend.Application.Configuration.Schemas;

namespace arch_forge_backend.Application.Simulation;

/// <summary>Why an element ended up in the state it did. Text is built by the engine, which knows the names.</summary>
public enum EffectReason
{
    /// <summary>The target answers. Nothing to report.</summary>
    Healthy,

    /// <summary>The fault was injected here (D4.1).</summary>
    Injected,

    /// <summary>Traffic no longer reaches it - phase B (plan técnico §6.2).</summary>
    Unreachable,

    /// <summary>The consumer of a queue that no longer delivers: alive and idle, not dead (D4.7).</summary>
    NoWork,

    /// <summary>The target is down, the call has a timeout, and there is no alternative (D4.2).</summary>
    Unprotected,

    /// <summary>No timeout: waits forever, and neither the retry nor the fallback get to run (D4.2).</summary>
    NoTimeout,

    /// <summary>The call costs more than its timeout, so it expires every time (D4.1).</summary>
    TimeoutExceeded,

    /// <summary>A fallback turned a Down into a Degraded, and the propagation stops here (D4.5).</summary>
    Contained,

    /// <summary>The work is not enqueued, but it keeps serving requests (D4.7).</summary>
    QueueDown,

    /// <summary>A dependency answers worse and drags the degradation upwards (D1.3).</summary>
    DegradedDependency,

    /// <summary>One instance is not answering and the health check routes around it (D4.8).</summary>
    InstanceDown
}

/// <summary>The three mechanisms that <em>contain</em>. The two that only bound - timeout and circuit breaker - never appear here (D2.2).</summary>
public static class Mechanisms
{
    public const string Fallback = "fallback";
    public const string Queue = "queue";
    public const string HealthCheck = "healthCheck";
}

/// <param name="State">What this dependency imposes on whoever calls it.</param>
/// <param name="Contains">
/// Whether a mechanism stopped the escalation here. A node that contained behaves upwards
/// <em>like a healthy one</em> - that is what containing means, and it is what makes the picture
/// readable: red at the core of the fault, amber at the border where it stopped, green beyond.
/// </param>
/// <param name="Mechanism">The name of the mechanism that contained, or null.</param>
public sealed record EdgeOutcome(HealthState State, bool Contains, string? Mechanism, EffectReason Reason)
{
    public static readonly EdgeOutcome Healthy = new(HealthState.Healthy, false, null, EffectReason.Healthy);
}

/// <summary>
/// V3 block 4 entire, as one pure function. The most important file of V3 and the easiest to test:
/// the table of the five configurations of D4.4 is literally a <c>[Theory]</c> with five cases and
/// <em>not one graph built</em>.
///
/// It is a function and not a pipeline of registered mechanisms on purpose (D5.3). Rules are
/// independent - each answers a different question, which is why their order does not matter - and
/// mechanisms are not: they compose over the same edge in a fixed order, with the timeout as the
/// gate to all the others. A pipeline would have to encode that order in its registration order,
/// which is exactly the fragility V2 removed when it moved to fixed phases.
/// </summary>
public static class EdgeEffect
{
    /// <param name="targetState">
    /// The dependency's state in the previous round. An edge severed by an injected <c>down</c>
    /// is passed as <see cref="HealthState.Down"/>: from the caller's side a partition and a dead
    /// target are the same call that does not come back.
    /// </param>
    /// <param name="targetContained">Whether the dependency stopped a fault with a mechanism (D4.5).</param>
    /// <param name="callExpires">
    /// The call costs more than its timeout, so it expires every time and behaves as if the target
    /// were down (D4.1). This is where injected latency turns into a change of state.
    /// </param>
    public static EdgeOutcome Of(
        bool synchronous,
        ResilienceConfiguration resilience,
        HealthState targetState,
        bool targetContained,
        bool callExpires = false)
    {
        // An expired call is indistinguishable from a dead target - that is the whole point of a
        // timeout, and the lesson of D4.1.
        var effective = callExpires ? HealthState.Down : targetState;

        if (effective == HealthState.Healthy)
            return EdgeOutcome.Healthy;

        // D4.5: whoever calls a node that contained gets an answer, so it sees a healthy one. The
        // table in §7 lists this below the asynchronous row, but §8.3 - the reference scenario the
        // same document pins down - needs it above: in resilient-async the queue's consumer
        // contains with a fallback, and reading the rows the other way round would make the queue,
        // both APIs and the balancer amber for a fault that demonstrably stopped two hops earlier.
        // Containment is a cap on what a dependency imposes, and there is nothing to cap here.
        if (effective == HealthState.Degraded && targetContained)
            return EdgeOutcome.Healthy;

        // D4.7, and the cleanest demo in the simulator: an asynchronous edge never propagates Down.
        // The producer keeps answering; what does not happen is the work afterwards. Mark the edge
        // asynchronous, simulate again, and half the architecture goes from red to green.
        if (!synchronous)
            return new EdgeOutcome(HealthState.Degraded, true, Mechanisms.Queue, EffectReason.QueueDown);

        if (effective == HealthState.Degraded)
            return new EdgeOutcome(HealthState.Degraded, false, null, EffectReason.DegradedDependency);

        // D4.2, the guard that makes the timeout the key to everything else: with no timeout the
        // call never returns, so there is no error to catch and no mechanism of this edge can run.
        // `retries: 5` without a timeout is `retries: 0`.
        if (resilience.TimeoutSeconds is null)
            return new EdgeOutcome(HealthState.Down, false, null, EffectReason.NoTimeout);

        // D4.5. The circuit breaker is deliberately not here: it bounds latency, it does not save
        // anybody (D4.4). Where an edge carries both, the mechanism that changed the state is the
        // fallback, and that is what the label says.
        if (resilience.Fallback)
            return new EdgeOutcome(HealthState.Degraded, true, Mechanisms.Fallback, EffectReason.Contained);

        return new EdgeOutcome(
            HealthState.Down,
            false,
            null,
            callExpires ? EffectReason.TimeoutExceeded : EffectReason.Unprotected);
    }

    /// <summary>
    /// D4.8. A LoadBalancer spreading over interchangeable instances does not need all of them:
    /// what one dead instance does depends entirely on whether there is a health check. This is the
    /// visual argument for why health checks exist, and today nothing reads the field.
    ///
    /// Which nodes count as instances is topological and nothing else (D3.4) - the same siblings
    /// <c>SpofRule</c> already counts, so the analyzer and the simulator share the notion.
    /// </summary>
    /// <param name="instances">The outcome of each edge towards an instance of the balanced group.</param>
    public static EdgeOutcome OfBalancedGroup(bool healthCheck, IReadOnlyList<EdgeOutcome> instances)
    {
        if (instances.Count == 0)
            return EdgeOutcome.Healthy;

        var worst = instances.MaxBy(Rank)!;

        // Nothing is down: the group behaves like any other dependency.
        if (worst.State != HealthState.Down)
            return worst;

        // Not even a health check saves what is no longer there.
        if (instances.All(i => i.State == HealthState.Down))
            return worst;

        return healthCheck
            ? new EdgeOutcome(HealthState.Degraded, true, Mechanisms.HealthCheck, EffectReason.InstanceDown)
            // Without it the balancer keeps sending traffic to the dead node, so the degradation
            // climbs all the way to the root.
            : new EdgeOutcome(HealthState.Degraded, false, null, EffectReason.InstanceDown);
    }

    /// <summary>The lattice order, with containment breaking ties so an outcome that contained never hides one that did not.</summary>
    private static int Rank(EdgeOutcome outcome) => (int)outcome.State * 2 + (outcome.Contains ? 0 : 1);
}
