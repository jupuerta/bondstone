using bondstone_backend.Application.Configuration.Schemas;

namespace bondstone_backend.Application.Simulation;

public enum EffectReason
{
    Healthy,

    Injected,

    Unreachable,

    NoWork,

    Unprotected,

    NoTimeout,

    TimeoutExceeded,

    Contained,

    QueueDown,

    DegradedDependency,

    BreakerOpen,

    InstanceDown,

    Saturated,

    Overloaded
}

public static class Mechanisms
{
    public const string Fallback = "fallback";
    public const string CircuitBreaker = "circuitBreaker";
    public const string Queue = "queue";
    public const string HealthCheck = "healthCheck";
}

public sealed record EdgeOutcome(HealthState State, bool Contains, string? Mechanism, EffectReason Reason)
{
    public static readonly EdgeOutcome Healthy = new(HealthState.Healthy, false, null, EffectReason.Healthy);
}

public static class EdgeEffect
{
    public static EdgeOutcome Of(
        bool synchronous,
        ResilienceConfiguration resilience,
        HealthState targetState,
        bool targetContained,
        bool callExpires = false)
    {
        var effective = callExpires ? HealthState.Down : targetState;

        if (effective == HealthState.Healthy)
            return EdgeOutcome.Healthy;

        if (effective == HealthState.Degraded && targetContained)
            return EdgeOutcome.Healthy;

        if (!synchronous)
            return new EdgeOutcome(HealthState.Degraded, true, Mechanisms.Queue, EffectReason.QueueDown);

        // A slow dependency is the failure a fallback does not stop: you are still calling, still
        // waiting and still answering with whatever comes back. A breaker does stop it — it trips and
        // stops calling — so it is the one mechanism that contains a degraded dependency, and the
        // only one the workshop can promise it for (QA 30-08, §10.3 point 13, decided 30-08).
        if (effective == HealthState.Degraded)
            return resilience.CircuitBreaker
                ? new EdgeOutcome(HealthState.Degraded, true, Mechanisms.CircuitBreaker, EffectReason.BreakerOpen)
                : new EdgeOutcome(HealthState.Degraded, false, null, EffectReason.DegradedDependency);

        if (resilience.TimeoutSeconds is null)
            return new EdgeOutcome(HealthState.Down, false, null, EffectReason.NoTimeout);

        if (resilience.Fallback)
            return new EdgeOutcome(HealthState.Degraded, true, Mechanisms.Fallback, EffectReason.Contained);

        return new EdgeOutcome(
            HealthState.Down,
            false,
            null,
            callExpires ? EffectReason.TimeoutExceeded : EffectReason.Unprotected);
    }

    public static EdgeOutcome OfBalancedGroup(bool healthCheck, IReadOnlyList<EdgeOutcome> instances)
    {
        if (instances.Count == 0)
            return EdgeOutcome.Healthy;

        var worst = instances.MaxBy(Rank)!;

        if (worst.State != HealthState.Down)
            return worst;

        if (instances.All(i => i.State == HealthState.Down))
            return worst;

        return healthCheck
            ? new EdgeOutcome(HealthState.Degraded, true, Mechanisms.HealthCheck, EffectReason.InstanceDown)
            : new EdgeOutcome(HealthState.Degraded, false, null, EffectReason.InstanceDown);
    }

    private static int Rank(EdgeOutcome outcome) => (int)outcome.State * 2 + (outcome.Contains ? 0 : 1);
}
