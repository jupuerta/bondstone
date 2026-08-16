using arch_forge_backend.Application.Export;
using arch_forge_backend.Domain.Enums;
using Xunit;
using static arch_forge_backend.Application.Tests.Export.ExportFixtures;

namespace arch_forge_backend.Application.Tests.Export;

public class MermaidEmitterTests
{
    [Fact]
    public void A_synchronous_edge_is_a_solid_arrow()
    {
        var api = Node("Api", NodeType.Api);
        var db = Node("Db", NodeType.Database);

        var diagram = MermaidEmitter.Emit([api, db], [Connection(api, db, ProtocolType.Sql)]);

        Assert.Contains("Api -->|Sql| Db", diagram);
    }

    [Fact]
    public void An_asynchronous_edge_is_a_dotted_arrow()
    {
        // D2.2: the distinction goes in the stroke, because it is the one four rules and the whole
        // simulation engine turn on.
        var api = Node("Api", NodeType.Api);
        var queue = Node("Events", NodeType.Queue);

        var diagram = MermaidEmitter.Emit([api, queue], [Connection(api, queue, ProtocolType.Amqp, synchronous: false)]);

        Assert.Contains("Api -.->|Amqp| Events", diagram);
    }

    [Fact]
    public void A_node_label_carries_its_type()
    {
        var payments = Node("Payments", NodeType.Service);

        Assert.Contains("Payments[\"Service: Payments\"]", MermaidEmitter.Emit([payments], []));
    }

    [Fact]
    public void Two_nodes_with_the_same_name_get_distinct_identifiers()
    {
        var first = Node("Api", NodeType.Api, id: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
        var second = Node("Api", NodeType.Api, id: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"));

        var diagram = MermaidEmitter.Emit([first, second], []);

        Assert.Contains("Api[\"Api: Api\"]", diagram);
        Assert.Contains("Api_2[\"Api: Api\"]", diagram);
    }

    [Fact]
    public void The_suffix_of_a_collision_follows_the_order_it_is_given()
    {
        // The forgotten half of ajuste 1. The suffix is assigned over the already-ordered list, so
        // the caller's order decides it and two runs over the same graph cannot swap them. Here the
        // two nodes differ only in name, so the edge tells the identifiers apart.
        var first = Node("Api", NodeType.Api, id: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
        var second = Node("Api", NodeType.Api, id: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"));

        Assert.Contains("Api -->|Http| Api_2", MermaidEmitter.Emit([first, second], [Connection(first, second)]));
        Assert.Contains("Api_2 -->|Http| Api", MermaidEmitter.Emit([second, first], [Connection(first, second)]));
    }

    [Fact]
    public void A_name_that_would_break_the_block_does_not()
    {
        var awkward = Node("Orders | Payments [v2]", NodeType.Service);

        var diagram = MermaidEmitter.Emit([awkward], []);

        Assert.DoesNotContain("|", diagram[diagram.IndexOf('[')..]);
        Assert.Contains("OrdersPaymentsv2[\"Service: Orders / Payments (v2)\"]", diagram);
    }

    [Fact]
    public void An_edge_pointing_at_a_node_that_is_not_there_is_dropped_without_taking_the_diagram_with_it()
    {
        var api = Node("Api", NodeType.Api);
        var ghost = Node("Ghost", NodeType.Service);

        var diagram = MermaidEmitter.Emit([api], [Connection(api, ghost)]);

        Assert.Contains("Api[\"Api: Api\"]", diagram);
        Assert.DoesNotContain("-->", diagram);
    }
}
