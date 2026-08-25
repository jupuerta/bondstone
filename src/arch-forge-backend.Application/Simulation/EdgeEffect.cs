using arch_forge_backend.Application.Configuration.Schemas;

namespace arch_forge_backend.Application.Simulation;

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

    InstanceDown,

    Saturated,

    Overloaded
}

public static class Mechanisms
{
    public const string Fallback = "fallback";
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

        if (effective == HealthState.Degraded)
            return new EdgeOutcome(HealthState.Degraded, false, null, EffectReason.DegradedDependency);

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
