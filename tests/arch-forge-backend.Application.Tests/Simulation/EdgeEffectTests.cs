using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Configuration.Schemas;
using arch_forge_backend.Application.Simulation;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation;

public class EdgeEffectTests
{
    private static ResilienceConfiguration Resilience(
        int? timeoutSeconds = null,
        int? retries = null,
        bool circuitBreaker = false,
        bool fallback = false) =>
        new()
        {
            TimeoutSeconds = timeoutSeconds,
            Retries = retries,
            CircuitBreaker = circuitBreaker,
            Fallback = fallback
        };

    [Theory]
    [InlineData(null, null, false, false, HealthState.Down, false, null)]
    [InlineData(5, null, false, false, HealthState.Down, false, null)]
    [InlineData(5, 3, false, false, HealthState.Down, false, null)]
    [InlineData(5, null, true, false, HealthState.Down, false, null)]
    [InlineData(5, null, false, true, HealthState.Degraded, true, "fallback")]
    [InlineData(5, 3, true, true, HealthState.Degraded, true, "fallback")]
    public void TheFiveConfigurationsOfD44(
        int? timeoutSeconds,
        int? retries,
        bool circuitBreaker,
        bool fallback,
        HealthState expected,
        bool contains,
        string? mechanism)
    {
        var outcome = EdgeEffect.Of(
            synchronous: true,
            Resilience(timeoutSeconds, retries, circuitBreaker, fallback),
            targetState: HealthState.Down,
            targetContained: false);

        Assert.Equal(expected, outcome.State);
        Assert.Equal(contains, outcome.Contains);
        Assert.Equal(mechanism, outcome.Mechanism);
    }

    [Fact]
    public void HealthyTarget_ImposesNothing()
    {
        var outcome = EdgeEffect.Of(true, Resilience(timeoutSeconds: 5), HealthState.Healthy, false);

        Assert.Equal(HealthState.Healthy, outcome.State);
        Assert.False(outcome.Contains);
    }

    [Fact]
    public void WithoutTimeout_NoMechanismOfTheEdgeCanAct()
    {
        var outcome = EdgeEffect.Of(
            true,
            Resilience(timeoutSeconds: null, retries: 5, circuitBreaker: true, fallback: true),
            HealthState.Down,
            false);

        Assert.Equal(HealthState.Down, outcome.State);
        Assert.False(outcome.Contains);
        Assert.Equal(EffectReason.NoTimeout, outcome.Reason);
    }

    [Fact]
    public void AsynchronousEdge_NeverPropagatesDown()
    {
        var outcome = EdgeEffect.Of(synchronous: false, Resilience(), HealthState.Down, false);

        Assert.Equal(HealthState.Degraded, outcome.State);
        Assert.True(outcome.Contains);
        Assert.Equal("queue", outcome.Mechanism);
    }

    [Fact]
    public void AsynchronousEdge_ContainsWithoutNeedingATimeout()
    {
        var outcome = EdgeEffect.Of(false, Resilience(timeoutSeconds: null), HealthState.Down, false);

        Assert.True(outcome.Contains);
    }

    [Fact]
    public void ATargetThatContained_LooksHealthyFromAbove()
    {
        var outcome = EdgeEffect.Of(true, Resilience(timeoutSeconds: 5), HealthState.Degraded, targetContained: true);

        Assert.Equal(HealthState.Healthy, outcome.State);
    }

    [Fact]
    public void ATargetDegradedForAnyOtherReason_DragsTheDegradationUp()
    {
        var outcome = EdgeEffect.Of(true, Resilience(timeoutSeconds: 5), HealthState.Degraded, targetContained: false);

        Assert.Equal(HealthState.Degraded, outcome.State);
        Assert.False(outcome.Contains);
        Assert.Equal(EffectReason.DegradedDependency, outcome.Reason);
    }

    [Fact]
    public void ACallThatExpires_BehavesLikeADeadTarget()
    {
        var outcome = EdgeEffect.Of(
            true,
            Resilience(timeoutSeconds: 5),
            targetState: HealthState.Healthy,
            targetContained: false,
            callExpires: true);

        Assert.Equal(HealthState.Down, outcome.State);
        Assert.Equal(EffectReason.TimeoutExceeded, outcome.Reason);
    }

    [Fact]
    public void ACallThatExpires_IsStillCaughtByAFallback()
    {
        var outcome = EdgeEffect.Of(true, Resilience(5, fallback: true), HealthState.Healthy, false, callExpires: true);

        Assert.Equal(HealthState.Degraded, outcome.State);
        Assert.True(outcome.Contains);
    }


    private static readonly EdgeOutcome DeadInstance = new(HealthState.Down, false, null, EffectReason.Unprotected);

    [Fact]
    public void BalancerWithHealthCheck_AbsorbsADeadInstanceAndStopsTheFault()
    {
        var outcome = EdgeEffect.OfBalancedGroup(healthCheck: true, [DeadInstance, EdgeOutcome.Healthy]);

        Assert.Equal(HealthState.Degraded, outcome.State);
        Assert.True(outcome.Contains);
        Assert.Equal("healthCheck", outcome.Mechanism);
    }

    [Fact]
    public void BalancerWithoutHealthCheck_KeepsSendingTrafficToTheDeadNode()
    {
        var outcome = EdgeEffect.OfBalancedGroup(healthCheck: false, [DeadInstance, EdgeOutcome.Healthy]);

        Assert.Equal(HealthState.Degraded, outcome.State);
        Assert.False(outcome.Contains);
    }

    [Fact]
    public void BalancerWithEveryInstanceDown_IsDown()
    {
        var outcome = EdgeEffect.OfBalancedGroup(healthCheck: true, [DeadInstance, DeadInstance]);

        Assert.Equal(HealthState.Down, outcome.State);
        Assert.False(outcome.Contains);
    }

    [Fact]
    public void BalancerWithEveryInstanceHealthy_ImposesNothing()
    {
        var outcome = EdgeEffect.OfBalancedGroup(true, [EdgeOutcome.Healthy, EdgeOutcome.Healthy]);

        Assert.Equal(HealthState.Healthy, outcome.State);
    }
}
