using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

/// <summary>
/// V15 E2. The rule the V15 sweep found missing with a single drawing: `timeoutSeconds: 600` - the
/// ceiling B14 itself introduced - on `Api → Db`, not one finding, and the diagram scored 99.
/// </summary>
public class TimeoutTooLongRuleTests
{
    private readonly TimeoutTooLongRule _rule = new();

    [Fact]
    public void TheSweepsDrawing_IsNoLongerSilence()
    {
        // The T7 of docs/V15/QA-analisis-V15.md: ten minutes counted as protection for
        // MISSING_RESILIENCE, which only asks whether a timeout exists, and nobody else looked.
        var b = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"authentication":true,"rateLimit":1000}""")
            .Node("Db", NodeType.Database, """{"replicas":2}""")
            .Connect("Api", "Db", timeoutSeconds: 600, fallback: true, protocol: ProtocolType.Sql);

        var finding = Assert.Single(_rule.Evaluate(b.Build()).Findings);

        Assert.Equal(RuleCodes.TimeoutTooLong, finding.Code);
        Assert.Equal(Area.Resilience, finding.Area);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.TIMEOUT_TOO_LONG.unbounded.message(source=Api, target=Db, timeoutMs=600000)", finding.Message.Describe());
        Assert.Equal("finding.TIMEOUT_TOO_LONG.unbounded.explanation", finding.Explanation.Key());
        Assert.Equal("30000", finding.Explanation.Arg("waitMs"));
        Assert.Equal(b.EdgeElement("Api", "Db"), Assert.Single(finding.Elements));

        // D5.6: lowering it is unambiguous, and the number is the product's default when the
        // measured call needs no more than that.
        Assert.Equal(FixKinds.SetEdgeResilience, finding.Fix?.Kind);
        Assert.Equal(5, finding.Fix?.TimeoutSeconds);
        Assert.Equal("fix.TIMEOUT_TOO_LONG.lower(seconds=5)", finding.Fix?.Label.Describe());
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(29, false)]
    [InlineData(30, true)]    // exactly the unbounded wait: protects as much as none
    [InlineData(600, true)]
    public void TheLine_IsTheSimulatorsUnboundedWait(int timeoutSeconds, bool fires)
    {
        // Not a number of this rule's own: LatencyModel.UnboundedWait is what a call with no
        // timeout has cost since V3, so a timeout there or above is a wait, not a limit.
        Assert.Equal(30_000, RuleThresholds.TimeoutUnboundedMs);
        Assert.Equal(LatencyModel.UnboundedWait, RuleThresholds.TimeoutUnboundedMs);

        var b = new GraphBuilder().Node("Api", NodeType.Api).Node("Svc", NodeType.Service)
            .Connect("Api", "Svc", timeoutSeconds: timeoutSeconds, fallback: true);

        var result = _rule.Evaluate(b.Build());

        Assert.True(result.IsApplicable);
        Assert.Equal(fires ? 1 : 0, result.Findings.Count);
    }

    [Fact]
    public void HeldOpenOnACriticalSpof_IsCritical_AndACoveredCallIsNot()
    {
        // The same three conditions MISSING_RESILIENCE escalates on: nothing covers the call, and
        // what it lands on takes real traffic down with it. Then the wait is every request.
        static GraphBuilder Shape(bool fallback) => new GraphBuilder()
            .Node("Gw", NodeType.Gateway).Node("Api", NodeType.Api).Node("Db", NodeType.Database).Node("Cache", NodeType.Cache)
            .Connect("Gw", "Api", timeoutSeconds: 600, fallback: fallback)
            .Connect("Api", "Db", timeoutSeconds: 3, fallback: true, protocol: ProtocolType.Sql)
            .Connect("Api", "Cache", timeoutSeconds: 1, fallback: true);

        var bare = Shape(fallback: false).Build();
        bare.Facts.Add(new SinglePointOfFailureFact(bare.NodeId("Api"), Severity.Critical));
        var critical = Assert.Single(_rule.Evaluate(bare).Findings);
        Assert.Equal(Severity.Critical, critical.Severity);
        Assert.Equal("finding.TIMEOUT_TOO_LONG.unboundedToSpof.explanation", critical.Explanation.Key());

        var covered = Shape(fallback: true).Build();
        covered.Facts.Add(new SinglePointOfFailureFact(covered.NodeId("Api"), Severity.Critical));
        var warning = Assert.Single(_rule.Evaluate(covered).Findings);
        Assert.Equal(Severity.Warning, warning.Severity);
        Assert.Equal("finding.TIMEOUT_TOO_LONG.unbounded.explanation", warning.Explanation.Key());

        // A Warning SPOF - a covered passage - does not buy the Critical either.
        var soft = Shape(fallback: false).Build();
        soft.Facts.Add(new SinglePointOfFailureFact(soft.NodeId("Api"), Severity.Warning));
        Assert.Equal(Severity.Warning, Assert.Single(_rule.Evaluate(soft).Findings).Severity);
    }

    [Fact]
    public void WhatEveryResilienceRuleLeavesAlone_ThisOneLeavesAlone()
    {
        // The outside's request, a third party's own call, the telemetry's scrape (V15 A3), and an
        // asynchronous hand-off: none of them is a wait this architect configures.
        var b = new GraphBuilder()
            .Node("Web", NodeType.Client).Node("Api", NodeType.Api).Node("Stripe", NodeType.ExternalService)
            .Node("Maps", NodeType.ExternalService).Node("Obs", NodeType.Observability).Node("Cola", NodeType.Queue)
            .Connect("Web", "Api", timeoutSeconds: 600)
            .Connect("Stripe", "Maps", timeoutSeconds: 600)
            .Connect("Obs", "Api", timeoutSeconds: 600)
            .Connect("Api", "Cola", timeoutSeconds: 600, synchronous: false, protocol: ProtocolType.Amqp)
            // The one call that is the architect's, with a sane timeout, so the rule is applicable
            // and the silence above is a check that passed rather than nothing to check.
            .Connect("Api", "Stripe", timeoutSeconds: 5, fallback: true);

        var result = _rule.Evaluate(b.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void WithoutASingleTimeout_ThereIsNothingToCompare()
    {
        var b = new GraphBuilder().Node("Api", NodeType.Api).Node("Db", NodeType.Database)
            .Connect("Api", "Db", protocol: ProtocolType.Sql);

        Assert.False(_rule.Evaluate(b.Build()).IsApplicable);
    }

    [Fact]
    public void TheFix_NeverCreatesATimeoutTooShortOfItsOwn()
    {
        // A long chain under load costs whole seconds; the fix lowers to twice that, rounded up,
        // and never under the product's default - so applying it does not hand the edge to the
        // twin rule.
        var b = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":285}""")
            .Nodes(NodeType.Service, "S1", "S2", "S3")
            .Connect("Api", "S1", timeoutSeconds: 120)
            .Connect("S1", "S2", timeoutSeconds: 120)
            .Connect("S2", "S3", timeoutSeconds: 120);

        var graph = b.Build();
        var findings = _rule.Evaluate(graph).Findings;
        var cost = CallCost.Of(graph, CostInputs.Nominal with { QueueingFactors = LoadModel.Of(graph).QueueingFactors });

        Assert.Equal(3, findings.Count);
        Assert.All(findings, f =>
        {
            var edge = graph.Connections.Single(c => c.Id == f.Elements[0].Id);
            var callMs = cost.Of(edge).RawLatencyMs;
            Assert.True(f.Fix!.TimeoutSeconds!.Value * 1000 > callMs, $"{f.Fix.TimeoutSeconds} s over a {callMs} ms call");
            Assert.True(f.Fix.TimeoutSeconds >= 5);
        });
    }

    [Theory]
    [InlineData("minimal-api-db")]
    [InlineData("resilient-async")]
    [InlineData("gateway-microservices")]
    [InlineData("worst-case")]
    public void TheReferences_KeepTheirFiveSecondTimeouts(string fixture)
    {
        // Every reference times its calls at 5 s over calls costing tens of milliseconds: limits,
        // not waits. The rule applies to all four and fires on none - which is the green tick each
        // one gains, and the only thing about them this rule changes.
        var result = ReferenceArchitecture.Analyze(fixture);
        var finding = Assert.Single(result.Findings, f => f.Code == RuleCodes.TimeoutTooLong);

        Assert.Equal(Severity.Good, finding.Severity);
    }
}
