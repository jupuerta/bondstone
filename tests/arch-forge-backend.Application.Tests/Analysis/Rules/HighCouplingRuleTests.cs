using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
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

    private void Star(string source, int dependencies)
    {
        _builder.Node(source);

        for (var i = 0; i < dependencies; i++)
        {
            _builder.Node($"D{i}");
            _builder.Connect(source, $"D{i}");
        }
    }
}
