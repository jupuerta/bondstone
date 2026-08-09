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
        Assert.Equal("SYNC_CHAIN", finding.RuleCode);
        Assert.Equal(a.Id, finding.NodeId);
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
