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
    public void Evaluate_TwoServicesDependOnSameDatabaseWithNoRedundancy_FlagsDatabaseAsSpof()
    {
        var database = NewNode(NodeType.Database, "Database");
        var orders = NewNode(NodeType.Service, "Orders");
        var payments = NewNode(NodeType.Service, "Payments");

        var graph = new ArchitectureGraph(
            [database, orders, payments],
            [Connect(orders, database), Connect(payments, database)]);

        var findings = _rule.Evaluate(graph).ToList();

        var finding = Assert.Single(findings);
        Assert.Equal("SPOF", finding.RuleCode);
        Assert.Equal(database.Id, finding.NodeId);
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

        var findings = _rule.Evaluate(graph).ToList();

        Assert.Empty(findings);
    }

    [Fact]
    public void Evaluate_SingleDependent_DoesNotFlagSpof()
    {
        var database = NewNode(NodeType.Database, "Database");
        var orders = NewNode(NodeType.Service, "Orders");

        var graph = new ArchitectureGraph([database, orders], [Connect(orders, database)]);

        var findings = _rule.Evaluate(graph).ToList();

        Assert.Empty(findings);
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
