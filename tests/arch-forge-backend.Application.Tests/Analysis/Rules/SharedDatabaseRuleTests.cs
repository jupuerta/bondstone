using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

/// <summary>
/// QA §4. Integration by database: two services meeting on one schema.
/// </summary>
public class SharedDatabaseRuleTests
{
    private readonly SharedDatabaseRule _rule = new();
    private readonly GraphBuilder _builder = new();

    [Fact]
    public void Two_services_on_one_database_are_flagged()
    {
        _builder
            .Nodes(NodeType.Service, "Users", "Orders")
            .Node("Db", NodeType.Database)
            .Connect("Users", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Orders", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(RuleCodes.SharedDatabase, finding.Code);
        Assert.Equal(Area.Coupling, finding.Area);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(
            "finding.SHARED_DATABASE.shared.message(count=2, names='Users', 'Orders', node=Db)",
            finding.Message.Describe());
        Assert.Equal("finding.SHARED_DATABASE.shared.explanation(node=Db)", finding.Explanation.Describe());
        Assert.Equal("finding.SHARED_DATABASE.shared.suggestion", finding.Suggestion.Key());
        Assert.Equal(
            [_builder.NodeElement("Db"), _builder.EdgeElement("Users", "Db"), _builder.EdgeElement("Orders", "Db")],
            finding.Elements);
        Assert.Null(finding.Fix);
    }

    [Fact]
    public void One_service_on_its_own_database_passes()
    {
        _builder
            .Node("Orders")
            .Node("Db", NodeType.Database)
            .Connect("Orders", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void An_api_and_a_worker_on_one_database_are_one_application()
    {
        // The ordinary shape of a single application with a background job. Only Services count:
        // the model cannot tell reads from writes, so the conservative reading is the one that never
        // fires on a diagram that is right (D4.3).
        _builder
            .Node("Api", NodeType.Api)
            .Node("Worker", NodeType.Worker)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Worker", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void An_api_beside_a_service_is_not_two_services()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Orders")
            .Node("Db", NodeType.Database)
            .Connect("Api", "Orders", timeoutSeconds: 3)
            .Connect("Api", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Orders", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void Two_edges_from_the_same_service_are_one_service()
    {
        _builder
            .Node("Orders")
            .Node("Db", NodeType.Database)
            .Connect("Orders", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Orders", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void Protected_calls_and_a_replicated_database_are_still_one_schema()
    {
        // This is what makes it a Coupling finding and not SPOF counted twice (HIGH_COUPLING's
        // D4.1): nothing here falls with anything - every call has a fallback, the database is
        // replicated - and the two services still cannot change a table without each other.
        _builder
            .Nodes(NodeType.Service, "Users", "Orders")
            .Node("Db", NodeType.Database, """{"replicas": 2}""")
            .Connect("Users", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql)
            .Connect("Orders", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql);

        Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
        Assert.Empty(new SpofRule().Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void A_bucket_two_services_share_is_integration_by_files()
    {
        // QA B3's reading: object storage is a system of record, and two services both reading and
        // writing one bucket are coupled through its layout exactly as through a schema.
        _builder
            .Nodes(NodeType.Service, "Ingest", "Reports")
            .Node("Files", NodeType.ObjectStorage)
            .Connect("Ingest", "Files", timeoutSeconds: 5)
            .Connect("Reports", "Files", timeoutSeconds: 5);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
        Assert.Equal("Files", finding.Message.Arg("node"));
    }

    [Fact]
    public void Each_shared_store_is_its_own_finding()
    {
        _builder
            .Nodes(NodeType.Service, "A", "B")
            .Nodes(NodeType.Database, "Db1", "Db2")
            .Connect("A", "Db1", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("B", "Db1", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("A", "Db2", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("B", "Db2", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        Assert.Equal(2, _rule.Evaluate(_builder.Build()).Findings.Count);
    }

    [Fact]
    public void Without_a_store_anybody_calls_there_is_nothing_to_check()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Orders")
            .Node("Db", NodeType.Database)
            .Connect("Api", "Orders", timeoutSeconds: 3);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }

    // ---- V15 B2: two replicas are one service ---------------------------------

    [Fact]
    public void Two_replicas_behind_a_balancer_are_one_service()
    {
        // QA V15 C4 (T3): `LB -> Pedidos1, Pedidos2 -> Db` said "'Db' es el almacén compartido de 2
        // servicios" and advised giving each its own data - which splits one service's database in
        // two. Boxes of the same type fanned out from the same LoadBalancer are the one kind of copy
        // the model recognises (SpofRule reads it the same way), and a copy is not another service.
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Service, "Pedidos1", "Pedidos2")
            .Node("Db", NodeType.Database)
            .Connect("LB", "Pedidos1", timeoutSeconds: 3).Connect("LB", "Pedidos2", timeoutSeconds: 3)
            .Connect("Pedidos1", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Pedidos2", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Two_services_behind_a_gateway_are_still_two_services()
    {
        // The trap the V15 plan names: IsRouter includes Gateway, and the services of
        // gateway-microservices and worst-case hang from one. A gateway routes to *different*
        // things; only a LoadBalancer spreads over copies. Grouping by the wider predicate would
        // erase the integration-by-database lesson from both references.
        _builder
            .Node("Gw", NodeType.Gateway)
            .Nodes(NodeType.Service, "Users", "Orders")
            .Node("Db", NodeType.Database)
            .Connect("Gw", "Users", timeoutSeconds: 3).Connect("Gw", "Orders", timeoutSeconds: 3)
            .Connect("Users", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Orders", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(
            "finding.SHARED_DATABASE.shared.message(count=2, names='Users', 'Orders', node=Db)",
            finding.Message.Describe());
    }

    [Fact]
    public void A_replica_set_beside_another_service_counts_once()
    {
        // Replicas of Pedidos and a separate Users on one schema: two services, not three. The card
        // counts and names one per set - the first drawn - and the canvas lights every arrow, the
        // same split MISSING_CACHE made between the count and Elements in V14 B7.
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Service, "Pedidos1", "Pedidos2", "Users")
            .Node("Db", NodeType.Database)
            .Connect("LB", "Pedidos1", timeoutSeconds: 3).Connect("LB", "Pedidos2", timeoutSeconds: 3)
            .Connect("Pedidos1", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Pedidos2", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Users", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(
            "finding.SHARED_DATABASE.shared.message(count=2, names='Pedidos1', 'Users', node=Db)",
            finding.Message.Describe());
        Assert.Equal(["Db", "Pedidos1->Db", "Pedidos2->Db", "Users->Db"], _builder.Names(finding.Elements));
    }

    [Fact]
    public void Two_replica_sets_are_two_services()
    {
        // Copies of Users behind one balancer, copies of Orders behind another: the sets are the
        // services, and two sets on one schema is the finding.
        _builder
            .Nodes(NodeType.LoadBalancer, "LbUsers", "LbOrders")
            .Nodes(NodeType.Service, "Users1", "Users2", "Orders1", "Orders2")
            .Node("Db", NodeType.Database)
            .Connect("LbUsers", "Users1", timeoutSeconds: 3).Connect("LbUsers", "Users2", timeoutSeconds: 3)
            .Connect("LbOrders", "Orders1", timeoutSeconds: 3).Connect("LbOrders", "Orders2", timeoutSeconds: 3)
            .Connect("Users1", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Users2", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Orders1", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Orders2", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(
            "finding.SHARED_DATABASE.shared.message(count=2, names='Users1', 'Orders1', node=Db)",
            finding.Message.Describe());
    }

    [Fact]
    public void A_balancer_over_one_box_makes_no_copy()
    {
        // Negative control on the grouping itself: one target behind the balancer is not a
        // replica set, so Users and Orders stay two services.
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Service, "Users", "Orders")
            .Node("Db", NodeType.Database)
            .Connect("LB", "Users", timeoutSeconds: 3)
            .Connect("Users", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Orders", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void Replicas_declaring_writes_are_one_writer()
    {
        // The strong variant counts sets too: two replicas of Orders writing and a separate
        // Billing writing are two writers, named one per set.
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Service, "Orders1", "Orders2", "Billing")
            .Node("Db", NodeType.Database)
            .Connect("LB", "Orders1", timeoutSeconds: 3).Connect("LB", "Orders2", timeoutSeconds: 3)
            .Connect("Orders1", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql, access: "write")
            .Connect("Orders2", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql, access: "write")
            .Connect("Billing", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql, access: "write");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(
            "finding.SHARED_DATABASE.writers.message(count=2, names='Orders1', 'Billing', node=Db)",
            finding.Message.Describe());
    }

    [Fact]
    public void Different_services_behind_one_balancer_read_as_copies_for_now()
    {
        // Pinned, not endorsed. "Same type behind the same balancer" is the drawing's only way of
        // saying "these are the same thing", and it cannot tell copies from two different services
        // somebody put behind one balancer (V14 B11, T14). The successor is a declared replicaOf /
        // replica group on the node; when it lands, this is the assertion to flip.
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Service, "Users", "Orders")
            .Node("Db", NodeType.Database)
            .Connect("LB", "Users", timeoutSeconds: 3).Connect("LB", "Orders", timeoutSeconds: 3)
            .Connect("Users", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Orders", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    // ---- V15 E3: the read lesson ---------------------------------------------

    [Fact]
    public void Declared_readers_get_the_read_lesson()
    {
        // QA V15 M2 (T8): three services with access "read" declared were told "the schema is the
        // interface between them, and changing it for one means touching the others" - the
        // integration-by-database sentence, with advice to split the store. Reading a shared
        // schema couples, but not with the same risk: nobody here can break anybody's data.
        _builder
            .Nodes(NodeType.Service, "Reports", "Search", "Audit")
            .Node("Db", NodeType.Database)
            .Connect("Reports", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql, access: "read")
            .Connect("Search", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql, access: "read")
            .Connect("Audit", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql, access: "read");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(
            "finding.SHARED_DATABASE.readers.message(count=3, names='Reports', 'Search', 'Audit', node=Db)",
            finding.Message.Describe());
        Assert.Equal("finding.SHARED_DATABASE.readers.explanation(node=Db)", finding.Explanation.Describe());
        Assert.Equal("finding.SHARED_DATABASE.readers.suggestion", finding.Suggestion.Key());
        Assert.Null(finding.Fix);
    }

    [Fact]
    public void One_owner_and_declared_readers_is_the_read_lesson()
    {
        // The everyday shape: Orders owns the schema, Reports reads it directly. The card names
        // the readers - the coupling is theirs to undo - and the canvas still lights every arrow.
        _builder
            .Nodes(NodeType.Service, "Orders", "Reports")
            .Node("Db", NodeType.Database)
            .Connect("Orders", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql, access: "write")
            .Connect("Reports", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql, access: "read");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(
            "finding.SHARED_DATABASE.readers.message(count=1, names='Reports', node=Db)",
            finding.Message.Describe());
        Assert.Equal(["Db", "Orders->Db", "Reports->Db"], _builder.Names(finding.Elements));
    }

    [Fact]
    public void An_undeclared_edge_beside_declared_readers_is_still_at_most_one_writer()
    {
        // Absent means both, so Orders may write - and it is still the only one that may. What
        // makes the read lesson true is that the drawing leaves a single possible writer.
        _builder
            .Nodes(NodeType.Service, "Orders", "Reports", "Search")
            .Node("Db", NodeType.Database)
            .Connect("Orders", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Reports", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql, access: "read")
            .Connect("Search", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql, access: "read");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(
            "finding.SHARED_DATABASE.readers.message(count=2, names='Reports', 'Search', node=Db)",
            finding.Message.Describe());
    }

    [Fact]
    public void Two_possible_writers_keep_the_conservative_sentence()
    {
        // Negative control: two readers beside two undeclared edges is two services that may each
        // be writing, and that is the shared sentence exactly as before - the read lesson is
        // never guessed from the default.
        _builder
            .Nodes(NodeType.Service, "Orders", "Billing", "Reports", "Search")
            .Node("Db", NodeType.Database)
            .Connect("Orders", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Billing", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Reports", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql, access: "read")
            .Connect("Search", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql, access: "read");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(
            "finding.SHARED_DATABASE.shared.message(count=4, names='Orders', 'Billing', 'Reports', 'Search', node=Db)",
            finding.Message.Describe());
    }

    [Fact]
    public void Declared_writers_outrank_the_read_lesson()
    {
        // Two declared writers and a reader: the full form wins, and the reader is not among the
        // names - the writers variant names who writes, as it did in V14.
        _builder
            .Nodes(NodeType.Service, "Orders", "Billing", "Reports")
            .Node("Db", NodeType.Database)
            .Connect("Orders", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql, access: "write")
            .Connect("Billing", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql, access: "write")
            .Connect("Reports", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql, access: "read");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(
            "finding.SHARED_DATABASE.writers.message(count=2, names='Orders', 'Billing', node=Db)",
            finding.Message.Describe());
    }
}
