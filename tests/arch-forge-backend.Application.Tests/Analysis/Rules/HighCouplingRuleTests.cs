using bondstone_backend.Application.Tests.TestDoubles;
using bondstone_backend.Application.Localization;
using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Analysis.Rules;
using bondstone_backend.Application.Tests.Analysis.Rules.Support;
using bondstone_backend.Domain.Enums;
using Xunit;

namespace bondstone_backend.Application.Tests.Analysis.Rules;

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
        Star("Orders", 4);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(
            ["Orders", "Orders->D0", "Orders->D1", "Orders->D2", "Orders->D3"],
            _builder.Names(finding.Elements));
    }

    [Fact]
    public void AsynchronousDependenciesCountToo()
    {
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
        _builder.Connect("Orders", "A", synchronous: false);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void NoNodeHasDependencies_IsNotApplicable()
    {
        _builder.Nodes(default, "A", "B");

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }


    [Theory]
    [InlineData(NodeType.Gateway, "finding.HIGH_COUPLING.fanOut.suggestion.route")]
    [InlineData(NodeType.LoadBalancer, "finding.HIGH_COUPLING.fanOut.suggestion.route")]
    [InlineData(NodeType.Service, "finding.HIGH_COUPLING.fanOut.suggestion.split")]
    [InlineData(NodeType.Api, "finding.HIGH_COUPLING.fanOut.suggestion.split")]
    [InlineData(NodeType.Worker, "finding.HIGH_COUPLING.fanOut.suggestion.split")]
    [InlineData(NodeType.Database, "finding.HIGH_COUPLING.fanOut.suggestion.review")]
    public void TheSuggestionIsPickedByType(NodeType type, string expected)
    {
        Star("Front", 12, type, distinctTargetTypes: true);

        Assert.Equal(expected, Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Suggestion.Key());
    }


    [Fact]
    public void AGatewayWithSevenRoutes_IsNotFlaggedAtAll()
    {
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
        Star("Edge", 12, NodeType.Gateway);

        Assert.Equal(Severity.Critical, Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Severity);
    }

    [Fact]
    public void ABalancerOverManyReplicas_CountsOneDependency()
    {
        Star("Front", 12, NodeType.LoadBalancer);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void ABalancerOverManyKindsOfThing_IsFlagged()
    {
        Star("Front", 8, NodeType.LoadBalancer, distinctTargetTypes: true);

        Assert.Equal(Severity.Warning, Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Severity);
    }

    [Fact]
    public void AGatewayCountsRoutesAndNotKinds()
    {
        Star("Edge", 8, NodeType.Gateway);

        Assert.Equal("8", Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Message.Arg("count"));
    }

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
