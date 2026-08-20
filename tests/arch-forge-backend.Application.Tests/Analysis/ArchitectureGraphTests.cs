using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis;

/// <summary>
/// V6 D1.1. The graph is where the order of an architecture is decided, because it is the only
/// thing every rule reads. EF Core returns the rows in whatever order it likes, and until V6 that
/// order travelled all the way to the findings the user sees.
/// </summary>
public class ArchitectureGraphTests
{
    [Fact]
    public void Nodes_AreOrderedByTypeThenNameThenId()
    {
        // Grouped by type and alphabetical inside it - the order an inventory is read in, and the
        // one the export already composes its component table in (V5 §6.2).
        var graph = new ArchitectureGraph(
            [
                Node("Zeta", NodeType.Service),
                Node("Db", NodeType.Database),
                Node("Alpha", NodeType.Service),
                Node("Api", NodeType.Api)
            ],
            []);

        Assert.Equal(["Api", "Db", "Alpha", "Zeta"], graph.Nodes.Select(n => n.Name));
    }

    [Fact]
    public void NodesSharingAName_AreOrderedById()
    {
        // Names are free text and nothing stops two components from sharing one, so Id is what
        // turns a partial order into a total one.
        var first = Node("Api", NodeType.Api, new Guid("11111111-1111-1111-1111-111111111111"));
        var second = Node("Api", NodeType.Api, new Guid("22222222-2222-2222-2222-222222222222"));

        var graph = new ArchitectureGraph([second, first], []);

        Assert.Equal([first.Id, second.Id], graph.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void Connections_AreOrderedBySourceThenTargetName()
    {
        // By name and not by Id: an edge is read as "who calls whom", so the list follows the flow.
        var api = Node("Api", NodeType.Api);
        var orders = Node("Orders", NodeType.Service);
        var users = Node("Users", NodeType.Service);
        var db = Node("Db", NodeType.Database);

        var graph = new ArchitectureGraph(
            [api, orders, users, db],
            [Edge(users, db), Edge(api, orders), Edge(orders, db), Edge(api, db)]);

        Assert.Equal(
            ["Api → Db", "Api → Orders", "Orders → Db", "Users → Db"],
            graph.Connections.Select(c => $"{graph.NodesById[c.SourceNodeId].Name} → {graph.NodesById[c.TargetNodeId].Name}"));
    }

    [Fact]
    public void ShufflingTheInput_GivesTheSameGraph()
    {
        // The property the whole decision exists for: what the database hands over cannot change
        // what the rules see.
        var api = Node("Api", NodeType.Api);
        var orders = Node("Orders", NodeType.Service);
        var db = Node("Db", NodeType.Database);
        Connection[] edges = [Edge(api, orders), Edge(orders, db), Edge(api, db)];

        var straight = new ArchitectureGraph([api, orders, db], edges);
        var shuffled = new ArchitectureGraph([db, orders, api], [edges[2], edges[0], edges[1]]);

        Assert.Equal(straight.Nodes.Select(n => n.Id), shuffled.Nodes.Select(n => n.Id));
        Assert.Equal(straight.Connections.Select(c => c.Id), shuffled.Connections.Select(c => c.Id));
        Assert.Equal(straight.RootNodes.Select(n => n.Id), shuffled.RootNodes.Select(n => n.Id));
    }

    [Fact]
    public void TheLookups_FollowTheOrderOfTheConnections()
    {
        // A rule walking the dependents of a node inherits this order, so ordering the list and
        // leaving the lookups built from the argument would have fixed half the problem.
        var db = Node("Db", NodeType.Database);
        var users = Node("Users", NodeType.Service);
        var api = Node("Api", NodeType.Api);
        var orders = Node("Orders", NodeType.Service);

        var graph = new ArchitectureGraph(
            [db, users, api, orders],
            [Edge(users, db), Edge(orders, db), Edge(api, db)]);

        Assert.Equal(
            ["Api", "Orders", "Users"],
            graph.IncomingByTarget[db.Id].Select(c => graph.NodesById[c.SourceNodeId].Name));
    }

    [Fact]
    public void AnEdgePointingAtAMissingNode_DoesNotThrow()
    {
        // Ordering is not the place to discover a broken graph: the edge sorts under the empty
        // name and whatever reads it later fails where the failure means something.
        var api = Node("Api", NodeType.Api);
        var dangling = new Connection
        {
            Id = Guid.NewGuid(),
            SourceNodeId = api.Id,
            TargetNodeId = Guid.NewGuid(),
            Protocol = ProtocolType.Http,
            Synchronous = true,
            Configuration = "{}"
        };

        var graph = new ArchitectureGraph([api], [dangling]);

        Assert.Equal(dangling.Id, Assert.Single(graph.Connections).Id);
    }

    private static Node Node(string name, NodeType type, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Type = type,
        Name = name,
        Configuration = "{}"
    };

    private static Connection Edge(Node source, Node target) => new()
    {
        Id = Guid.NewGuid(),
        SourceNodeId = source.Id,
        TargetNodeId = target.Id,
        Protocol = ProtocolType.Http,
        Synchronous = true,
        Configuration = "{}"
    };
}
