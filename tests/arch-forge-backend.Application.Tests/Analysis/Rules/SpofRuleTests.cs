using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

public class SpofRuleTests
{
    private readonly SpofRule _rule = new();
    private readonly GraphBuilder _builder = new();

    [Fact]
    public void PatternA_ThreeDependents_IsCritical()
    {
        _builder
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Service, "Orders", "Payments", "Users")
            .Connect("Orders", "Db").Connect("Payments", "Db").Connect("Users", "Db");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(RuleCodes.Spof, finding.Code);
        Assert.Equal(Area.Availability, finding.Area);
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.SPOF.shared.message(count=3, node=Db)", finding.Message.Describe());
        Assert.Equal(_builder.NodeElement("Db"), Assert.Single(finding.Elements));
    }

    [Fact]
    public void PatternA_ExactlyTwoDependents_IsWarning()
    {
        // Severity by blast radius (D4.x): two dependents is not the same failure as ten.
        _builder
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Db").Connect("Payments", "Db");

        Assert.Equal(Severity.Warning, Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Severity);
    }

    [Fact]
    public void PatternA_SingleDependent_PassesInsteadOfFiring()
    {
        // The rule had something to check and it was fine - a green check, not silence (D1.4).
        // Were this "not applicable", Availability could never reach a clean 100.
        _builder
            .Node("Db", NodeType.Database)
            .Node("Api", NodeType.Api)
            .Connect("Api", "Db");

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void PatternB_GatewayWithNoDependents_IsCritical()
    {
        // The flagship example (§18, Post 4): nobody points at a gateway, so pattern A sees
        // nothing. Remove it and everything behind it becomes unreachable.
        _builder
            .Node("Gateway", NodeType.Gateway)
            .Nodes(NodeType.Service, "Users", "Orders", "Payments")
            .Connect("Gateway", "Users").Connect("Gateway", "Orders").Connect("Gateway", "Payments");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.SPOF.unreachable.message", finding.Message.Key());
        Assert.Equal("3", finding.Message.Arg("count"));
        Assert.Equal(_builder.NodeElement("Gateway"), Assert.Single(finding.Elements));
    }

    [Fact]
    public void PatternB_OnlyOneNodeBehindIt_DoesNotFire()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void PatternB_AlternativePathKeepsEverythingReachable_DoesNotFire()
    {
        // Api1 is not a point of passage: without it, Cache and Queue are still reachable via Api2.
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Node("Cache", NodeType.Cache)
            .Node("Queue", NodeType.Queue)
            .Connect("LB", "Api1").Connect("LB", "Api2")
            .Connect("Api1", "Cache").Connect("Api2", "Cache")
            .Connect("Api1", "Queue").Connect("Api2", "Queue");

        var findings = _rule.Evaluate(_builder.Build()).Findings;

        Assert.DoesNotContain(findings, f => f.Elements.Contains(_builder.NodeElement("Api1")));
    }

    [Fact]
    public void BothPatterns_ReportOneFindingWithTheHighestSeverity()
    {
        // Orders has two dependents (pattern A, Warning) and is the only way through to what is
        // behind it (pattern B, Critical). One finding per node, the worse severity (D4.x).
        _builder
            .Node("Gateway", NodeType.Gateway)
            .Nodes(NodeType.Service, "Orders", "Payments", "Inventory")
            .Connect("Gateway", "Orders")
            .Connect("Orders", "Payments")
            .Connect("Payments", "Inventory")
            .Connect("Inventory", "Orders");

        var findings = _rule.Evaluate(_builder.Build()).Findings;

        var orders = Assert.Single(findings, f => f.Elements.Contains(_builder.NodeElement("Orders")));
        Assert.Equal(Severity.Critical, orders.Severity);
        Assert.Equal("finding.SPOF.unreachable.message", orders.Message.Key());
    }

    [Fact]
    public void LoadBalancerSpreadingOverTwoNodesOfTheSameType_IsNeverFlagged()
    {
        // Without this exception the model loops: you add a balancer to fix a SPOF and the
        // balancer becomes the SPOF, with no way out (D4.x).
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Connect("LB", "Api1").Connect("LB", "Api2");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void LoadBalancerWithASingleTarget_IsNotExempt()
    {
        // It balances nothing, so it is a point of passage like any other.
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("LB", "Api").Connect("Api", "Db");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(_builder.NodeElement("LB"), Assert.Single(finding.Elements));
    }

    [Fact]
    public void ExplicitRedundancy_NodeBehindABalancer_IsExempt()
    {
        // Api1 and Api2 each serve both consumers, and the balancer is what makes them
        // interchangeable.
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("LB", "Api1").Connect("LB", "Api2")
            .Connect("Orders", "Api1").Connect("Orders", "Api2")
            .Connect("Payments", "Api1").Connect("Payments", "Api2");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void EquivalentSibling_SameTypeAndSameDependents_IsExempt()
    {
        _builder
            .Nodes(NodeType.Database, "Db", "DbReplica")
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Db").Connect("Orders", "DbReplica")
            .Connect("Payments", "Db").Connect("Payments", "DbReplica");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void SiblingsOfTheSameTypeServingDifferentConsumers_AreNotRedundant()
    {
        // V1 asked only for "some dependent in common", which with microservices exempted Users,
        // Orders and Payments from each other for hanging off the same gateway (D4.x).
        _builder
            .Nodes(NodeType.Database, "Db", "Reporting")
            .Nodes(NodeType.Service, "Orders", "Payments", "Users")
            .Connect("Orders", "Db").Connect("Orders", "Reporting")
            .Connect("Payments", "Db").Connect("Users", "Db");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(_builder.NodeElement("Db"), Assert.Single(finding.Elements));
    }

    [Fact]
    public void Evaluate_PublishesOneFactPerFindingCarryingItsSeverity()
    {
        // MISSING_RESILIENCE escalates only towards a Critical SPOF, so the fact and the finding
        // must agree on the severity (D5.1).
        _builder
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Db").Connect("Payments", "Db");

        var graph = _builder.Build();
        var findings = _rule.Evaluate(graph).Findings;

        var fact = Assert.IsType<SinglePointOfFailureFact>(Assert.Single(graph.Facts));
        Assert.Equal(_builder.IdOf("Db"), fact.NodeId);
        Assert.Equal(Assert.Single(findings).Severity, fact.Severity);
    }

    [Fact]
    public void Evaluate_NoNodeHasDependents_IsNotApplicable()
    {
        _builder.Node("Api", NodeType.Api).Node("Db", NodeType.Database);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }
}
