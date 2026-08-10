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
    public void Evaluate_ThreeComponentChain_FlagsChainStartAsWarning()
    {
        // ArchForge_decisiones_arquitectura_V1_1.md §3.1: API -> Orders -> Payments is length 3.
        var api = NewNode("API");
        var orders = NewNode("Orders");
        var payments = NewNode("Payments");

        var graph = new ArchitectureGraph(
            [api, orders, payments],
            [Connect(api, orders, synchronous: true), Connect(orders, payments, synchronous: true)]);

        var finding = Assert.Single(_rule.Evaluate(graph).Findings);

        Assert.Equal(RuleCodes.SyncChain, finding.Code);
        Assert.Equal(Area.Resilience, finding.Area);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("3 componentes", finding.Message);
    }

    [Fact]
    public void Evaluate_FiveComponentChain_EscalatesToCritical()
    {
        // D4.2: the cut at 5 is the literal example of §8.1 of the ideas document.
        var nodes = Enumerable.Range(0, 5).Select(i => NewNode($"N{i}")).ToList();
        var edges = nodes.Zip(nodes.Skip(1), (from, to) => Connect(from, to, synchronous: true)).ToList();

        var graph = new ArchitectureGraph(nodes, edges);

        var finding = Assert.Single(_rule.Evaluate(graph).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Contains("5 componentes", finding.Message);
    }

    [Fact]
    public void Evaluate_ChainWithSeveralHops_ReportsOnlyTheStartingNodeAndInterleavesTheWalk()
    {
        var a = NewNode("A");
        var b = NewNode("B");
        var c = NewNode("C");
        var d = NewNode("D");

        var ab = Connect(a, b, synchronous: true);
        var bc = Connect(b, c, synchronous: true);
        var cd = Connect(c, d, synchronous: true);

        var graph = new ArchitectureGraph([a, b, c, d], [ab, bc, cd]);

        var finding = Assert.Single(_rule.Evaluate(graph).Findings);

        Assert.Contains("4 componentes", finding.Message);

        // D2.2: node, edge, node, edge... so the canvas highlights by iterating.
        Assert.Equal(
            [
                FindingElement.Node(a.Id),
                FindingElement.Connection(ab.Id),
                FindingElement.Node(b.Id),
                FindingElement.Connection(bc.Id),
                FindingElement.Node(c.Id),
                FindingElement.Connection(cd.Id),
                FindingElement.Node(d.Id)
            ],
            finding.Elements);
    }

    [Fact]
    public void Evaluate_ShortChain_IsApplicableAndFindsNothing()
    {
        var a = NewNode("A");
        var b = NewNode("B");

        var graph = new ArchitectureGraph([a, b], [Connect(a, b, synchronous: true)]);

        var result = _rule.Evaluate(graph);

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
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

        Assert.Empty(_rule.Evaluate(graph).Findings);
    }

    [Fact]
    public void Evaluate_NoSynchronousEdge_IsNotApplicable()
    {
        var a = NewNode("A");
        var b = NewNode("B");

        var graph = new ArchitectureGraph([a, b], [Connect(a, b, synchronous: false)]);

        Assert.False(_rule.Evaluate(graph).IsApplicable);
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
