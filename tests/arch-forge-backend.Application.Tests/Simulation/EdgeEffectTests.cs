using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Configuration.Schemas;
using arch_forge_backend.Application.Simulation;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation;

/// <summary>
/// V3 D5.5: the test that is worth the most and costs the least. The whole semantics of block 4 is
/// fixed here, <strong>without building a single graph</strong>; everything else in the simulator is
/// a consequence.
///
/// When one of these fails, the first job is to work out which of the two is wrong - the table in
/// §7 of the plan or the code - not to adjust the expected value until it passes.
/// </summary>
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

    /// <summary>
    /// D4.4's table, from "nothing" to "all four mechanisms", exactly as the document writes it.
    /// It is the best demonstration of why the four are needed: five configurations, five different
    /// outcomes. Latency and amplification are checked by <see cref="CallCostTests"/>, which is the
    /// other half of the same table.
    /// </summary>
    [Theory]
    // configuration                        caller's state           contains  mechanism
    [InlineData(null, null, false, false, HealthState.Down, false, null)]        // nothing
    [InlineData(5, null, false, false, HealthState.Down, false, null)]           // timeout
    [InlineData(5, 3, false, false, HealthState.Down, false, null)]              // timeout + retries
    [InlineData(5, null, true, false, HealthState.Down, false, null)]            // timeout + breaker
    [InlineData(5, null, false, true, HealthState.Degraded, true, "fallback")]   // timeout + fallback
    [InlineData(5, 3, true, true, HealthState.Degraded, true, "fallback")]       // all four
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
        // D4.2, the guard that turns one box into the key to the others: there is no error to
        // catch, because the call does not come back. Not even a fallback gets to run.
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
        // D4.7 and Post 5 in one line. Take the edge, mark it asynchronous, simulate again, and
        // half the architecture goes from red to green.
        var outcome = EdgeEffect.Of(synchronous: false, Resilience(), HealthState.Down, false);

        Assert.Equal(HealthState.Degraded, outcome.State);
        Assert.True(outcome.Contains);
        Assert.Equal("queue", outcome.Mechanism);
    }

    [Fact]
    public void AsynchronousEdge_ContainsWithoutNeedingATimeout()
    {
        // The guard of D4.2 is about capturing an error from a call that never returns; an
        // asynchronous producer never waits for one, so the guard does not apply to it. That is
        // why §7 lists the asynchronous row above the whole table.
        var outcome = EdgeEffect.Of(false, Resilience(timeoutSeconds: null), HealthState.Down, false);

        Assert.True(outcome.Contains);
    }

    [Fact]
    public void ATargetThatContained_LooksHealthyFromAbove()
    {
        // D4.5, and the reason the picture reads by itself: red at the core, amber at the border
        // where it stopped, green beyond. Without this rule a fault in a leaf tints the whole
        // architecture and erases the only information that matters - where it stopped.
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
        // D4.1: injected latency changes no state by itself. It changes one when the call ends up
        // costing more than its timeout - which is the whole argument for timeouts, produced
        // without simulating a second.
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

    // ---------------------------------------------------------------------------------------
    // D4.8 - the load balancer, the only place where a dependency is not necessary but replaceable.
    // ---------------------------------------------------------------------------------------

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
        // The degradation climbs to the root, which is the visual argument for ticking the box:
        // it changes the colour of everything above the balancer.
        var outcome = EdgeEffect.OfBalancedGroup(healthCheck: false, [DeadInstance, EdgeOutcome.Healthy]);

        Assert.Equal(HealthState.Degraded, outcome.State);
        Assert.False(outcome.Contains);
    }

    [Fact]
    public void BalancerWithEveryInstanceDown_IsDown()
    {
        // Not even a health check saves what is no longer there.
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
