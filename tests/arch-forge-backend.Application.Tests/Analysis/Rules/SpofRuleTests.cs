using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

public class SpofRuleTests
{
    private readonly SpofRule _rule = new();

    [Fact]
    public void Evaluate_ThreeServicesDependOnSameDatabaseWithNoRedundancy_FlagsDatabaseAsCriticalSpof()
    {
        var database = NewNode(NodeType.Database, "Database");
        var orders = NewNode(NodeType.Service, "Orders");
        var payments = NewNode(NodeType.Service, "Payments");
        var users = NewNode(NodeType.Service, "Users");

        var graph = new ArchitectureGraph(
            [database, orders, payments, users],
            [Connect(orders, database), Connect(payments, database), Connect(users, database)]);

        var result = _rule.Evaluate(graph);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(RuleCodes.Spof, finding.Code);
        Assert.Equal(Area.Availability, finding.Area);
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal(FindingElement.Node(database.Id), Assert.Single(finding.Elements));
    }

    [Fact]
    public void Evaluate_ExactlyTwoDependents_FlagsAsWarning()
    {
        // Severity by blast radius (D4.x): two dependents is not the same failure as ten.
        var database = NewNode(NodeType.Database, "Database");
        var orders = NewNode(NodeType.Service, "Orders");
        var payments = NewNode(NodeType.Service, "Payments");

        var graph = new ArchitectureGraph(
            [database, orders, payments],
            [Connect(orders, database), Connect(payments, database)]);

        var finding = Assert.Single(_rule.Evaluate(graph).Findings);

        Assert.Equal(Severity.Warning, finding.Severity);
    }

    [Fact]
    public void Evaluate_PublishesFactCarryingTheSeverity()
    {
        // MISSING_RESILIENCE escalates only towards a Critical SPOF, so the severity travels
        // inside the fact (D5.1).
        var database = NewNode(NodeType.Database, "Database");
        var orders = NewNode(NodeType.Service, "Orders");
        var payments = NewNode(NodeType.Service, "Payments");

        var graph = new ArchitectureGraph(
            [database, orders, payments],
            [Connect(orders, database), Connect(payments, database)]);

        _rule.Evaluate(graph);

        var fact = Assert.IsType<SinglePointOfFailureFact>(Assert.Single(graph.Facts));
        Assert.Equal(database.Id, fact.NodeId);
        Assert.Equal(Severity.Warning, fact.Severity);
    }

    [Fact]
    public void Evaluate_RedundantSiblingCoveringSameDependent_DoesNotFlagSpof()
    {
        var database = NewNode(NodeType.Database, "Database");
        var databaseReplica = NewNode(NodeType.Database, "DatabaseReplica");
        var orders = NewNode(NodeType.Service, "Orders");
        var payments = NewNode(NodeType.Service, "Payments");

        var graph = new ArchitectureGraph(
            [database, databaseReplica, orders, payments],
            [
                Connect(orders, database),
                Connect(payments, database),
                Connect(orders, databaseReplica)
            ]);

        var result = _rule.Evaluate(graph);

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Evaluate_SingleDependent_IsApplicableAndFindsNothing()
    {
        // Applicable, because there is a node with dependents to check - it just passes (D1.4).
        var database = NewNode(NodeType.Database, "Database");
        var orders = NewNode(NodeType.Service, "Orders");

        var graph = new ArchitectureGraph([database, orders], [Connect(orders, database)]);

        var result = _rule.Evaluate(graph);

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Evaluate_NoNodeHasDependents_IsNotApplicable()
    {
        var graph = new ArchitectureGraph([NewNode(NodeType.Api, "Api")], []);

        Assert.False(_rule.Evaluate(graph).IsApplicable);
    }

    private static Node NewNode(NodeType type, string name) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Name = name
    };

    private static Connection Connect(Node source, Node target) => new()
    {
        Id = Guid.NewGuid(),
        SourceNodeId = source.Id,
        TargetNodeId = target.Id,
        Protocol = ProtocolType.Http,
        Synchronous = true
    };
}
