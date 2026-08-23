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
}
