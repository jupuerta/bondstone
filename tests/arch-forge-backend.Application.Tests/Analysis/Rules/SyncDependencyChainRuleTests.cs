using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

public class SyncDependencyChainRuleTests
{
    private readonly SyncDependencyChainRule _rule = new();

    [Fact]
    public void Evaluate_ThreeSynchronousHops_FlagsChainStart()
    {
        var a = NewNode("A");
        var b = NewNode("B");
        var c = NewNode("C");
        var d = NewNode("D");

        var graph = new ArchitectureGraph(
            [a, b, c, d],
            [Connect(a, b, synchronous: true), Connect(b, c, synchronous: true), Connect(c, d, synchronous: true)]);

        var findings = _rule.Evaluate(graph).ToList();

        var finding = Assert.Single(findings);
        Assert.Equal("SYNC_CHAIN", finding.Code);
        Assert.Equal(a.Id, finding.NodeId);
    }

    [Fact]
    public void Evaluate_ThreeComponentChain_Flags()
    {
        // ArchForge_decisiones_arquitectura_V1_1.md §3.1: API -> Orders -> Payments is length 3.
        var api = NewNode("API");
        var orders = NewNode("Orders");
        var payments = NewNode("Payments");

        var graph = new ArchitectureGraph(
            [api, orders, payments],
            [Connect(api, orders, synchronous: true), Connect(orders, payments, synchronous: true)]);

        var finding = Assert.Single(_rule.Evaluate(graph).ToList());

        Assert.Equal(api.Id, finding.NodeId);
        Assert.Contains("3 componentes", finding.Message);
    }

    [Fact]
    public void Evaluate_ChainWithSeveralHops_ReportsOnlyTheStartingNode()
    {
        var a = NewNode("A");
        var b = NewNode("B");
        var c = NewNode("C");
        var d = NewNode("D");

        var graph = new ArchitectureGraph(
            [a, b, c, d],
            [Connect(a, b, synchronous: true), Connect(b, c, synchronous: true), Connect(c, d, synchronous: true)]);

        var finding = Assert.Single(_rule.Evaluate(graph).ToList());

        Assert.Equal(a.Id, finding.NodeId);
        Assert.Contains("4 componentes", finding.Message);
    }

    [Fact]
    public void Evaluate_ShortChain_DoesNotFlag()
    {
        var a = NewNode("A");
        var b = NewNode("B");

        var graph = new ArchitectureGraph([a, b], [Connect(a, b, synchronous: true)]);

        var findings = _rule.Evaluate(graph).ToList();

        Assert.Empty(findings);
    }

    [Fact]
    public void Evaluate_AsynchronousHopBreaksTheChain()
    {
        var a = NewNode("A");
        var b = NewNode("B");
        var c = NewNode("C");
        var d = NewNode("D");

        var graph = new ArchitectureGraph(
            [a, b, c, d],
            [Connect(a, b, synchronous: true), Connect(b, c, synchronous: false), Connect(c, d, synchronous: true)]);

        var findings = _rule.Evaluate(graph).ToList();

        Assert.Empty(findings);
    }

    private static Node NewNode(string name) => new()
    {
        Id = Guid.NewGuid(),
        Type = NodeType.Service,
        Name = name
    };

    private static Connection Connect(Node source, Node target, bool synchronous) => new()
    {
        Id = Guid.NewGuid(),
        SourceNodeId = source.Id,
        TargetNodeId = target.Id,
        Protocol = ProtocolType.Http,
        Synchronous = synchronous
    };
}
