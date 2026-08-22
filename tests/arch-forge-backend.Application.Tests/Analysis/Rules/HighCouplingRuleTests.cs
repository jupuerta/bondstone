using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

public class HighCouplingRuleTests
{
    private readonly HighCouplingRule _rule = new();
    private readonly GraphBuilder _builder = new();

    [Fact]
    public void FanOutOfFour_IsAWarning()
    {
        Star("Orders", 4);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(RuleCodes.HighCoupling, finding.Code);
        Assert.Equal(Area.Coupling, finding.Area);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.HIGH_COUPLING.fanOut.message(count=4, node=Orders)", finding.Message.Describe());
    }

    [Fact]
    public void FanOutOfSeven_EscalatesToCritical()
    {
        Star("Orders", 7);

        Assert.Equal(Severity.Critical, Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Severity);
    }

    [Fact]
    public void FanOutOfThree_Passes()
    {
        Star("Orders", 3);

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void OneFindingPerCoupledNode_WithAllItsEdgesInside()
    {
        // D4.5, granularity: one finding per culprit element, never one per dependency.
        Star("Orders", 4);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(
            ["Orders", "Orders->D0", "Orders->D1", "Orders->D2", "Orders->D3"],
            _builder.Names(finding.Elements));
    }

    [Fact]
    public void AsynchronousDependenciesCountToo()
    {
        // Fan-out measures how many pieces a component needs, whatever the protocol - unlike the
        // resilience rules, which are guided exclusively by Synchronous.
        _builder.Node("Orders").Nodes(default, "A", "B", "C", "D");
        _builder.Connect("Orders", "A").Connect("Orders", "B", synchronous: false);
        _builder.Connect("Orders", "C").Connect("Orders", "D", synchronous: false);

        Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void TwoEdgesTowardsTheSameComponent_CountAsOneDependency()
    {
        _builder.Node("Orders").Nodes(default, "A", "B", "C");
        _builder.Connect("Orders", "A").Connect("Orders", "B").Connect("Orders", "C");
        _builder.Connect("Orders", "A", synchronous: false, protocol: Domain.Enums.ProtocolType.Amqp);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void NoNodeHasDependencies_IsNotApplicable()
    {
        _builder.Nodes(default, "A", "B");

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }

    // ---- V13 D1.2: the same count, three different things to say --------------

    [Theory]
    [InlineData(NodeType.Gateway, "finding.HIGH_COUPLING.fanOut.suggestion.route")]
    [InlineData(NodeType.LoadBalancer, "finding.HIGH_COUPLING.fanOut.suggestion.route")]
    [InlineData(NodeType.Service, "finding.HIGH_COUPLING.fanOut.suggestion.split")]
    [InlineData(NodeType.Api, "finding.HIGH_COUPLING.fanOut.suggestion.split")]
    [InlineData(NodeType.Worker, "finding.HIGH_COUPLING.fanOut.suggestion.split")]
    [InlineData(NodeType.Database, "finding.HIGH_COUPLING.fanOut.suggestion.review")]
    public void TheSuggestionIsPickedByType(NodeType type, string expected)
    {
        // Enough fan-out to fire under either bar (D2.2), so this test is about the sentence and
        // nothing else.
        Star("Front", 12, type, distinctTargetTypes: true);

        Assert.Equal(expected, Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Suggestion.Key());
    }

    // ---- V13 D2.2: fanning out is a router's job ------------------------------

    [Fact]
    public void AGatewayWithSevenRoutes_IsNotFlaggedAtAll()
    {
        // Seven is Critical on a Service and silence on a Gateway, and that is the decision: a
        // gateway with seven services behind it is a gateway with seven routes.
        Star("Edge", 7, NodeType.Gateway);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void AGatewayWithEightRoutes_IsAWarning()
    {
        Star("Edge", 8, NodeType.Gateway);

        Assert.Equal(Severity.Warning, Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Severity);
    }

    [Fact]
    public void AGatewayWithTwelveRoutes_IsStillCritical()
    {
        // Doubling the bar is not removing it: twelve things behind one door is the front of a
        // monolith, and the rule can still say so.
        Star("Edge", 12, NodeType.Gateway);

        Assert.Equal(Severity.Critical, Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Severity);
    }

    [Fact]
    public void ABalancerOverManyReplicas_CountsOneDependency()
    {
        // **The rule punishing its own advice.** Twelve instances of the same Api behind a balancer
        // is the redundancy this analyzer spends its time recommending, drawn twelve times. SPOF has
        // exempted "the redundancy mechanism itself" since V2 D4.x; this is that sentence applied to
        // fan-out.
        Star("Front", 12, NodeType.LoadBalancer);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void ABalancerOverManyKindsOfThing_IsFlagged()
    {
        // The other half: a balancer in front of eight different kinds of component is not
        // balancing anything, and counting kinds is what lets the rule tell the two apart.
        Star("Front", 8, NodeType.LoadBalancer, distinctTargetTypes: true);

        Assert.Equal(Severity.Warning, Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Severity);
    }

    [Fact]
    public void AGatewayCountsRoutesAndNotKinds()
    {
        // The balancer's rule is not extended to the gateway: two Services behind a gateway are two
        // routes, not two copies of anything.
        Star("Edge", 8, NodeType.Gateway);

        Assert.Equal("8", Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Message.Arg("count"));
    }

    /// <summary>
    /// Eight types that are not <see cref="NodeType.LoadBalancer"/>, so a balancer fronting them
    /// counts eight kinds rather than eight copies (D2.2).
    /// </summary>
    private static readonly NodeType[] DistinctTypes =
    [
        NodeType.Api, NodeType.Service, NodeType.Worker, NodeType.Database,
        NodeType.Cache, NodeType.Queue, NodeType.ObjectStorage, NodeType.Gateway
    ];

    private void Star(string source, int dependencies, NodeType type = NodeType.Service, bool distinctTargetTypes = false)
    {
        _builder.Node(source, type);

        for (var i = 0; i < dependencies; i++)
        {
            _builder.Node($"D{i}", distinctTargetTypes ? DistinctTypes[i % DistinctTypes.Length] : NodeType.Service);
            _builder.Connect(source, $"D{i}");
        }
    }
}
