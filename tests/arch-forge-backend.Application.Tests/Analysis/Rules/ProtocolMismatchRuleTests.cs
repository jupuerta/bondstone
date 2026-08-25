using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

public class ProtocolMismatchRuleTests
{
    private readonly ProtocolMismatchRule _rule = new();
    private readonly GraphBuilder _builder = new();

    [Fact]
    public void SqlIntoSomethingThatIsNotADatabase_IsCritical()
    {
        _builder
            .Node("Orders", NodeType.Service)
            .Node("Queue", NodeType.Queue)
            .Connect("Orders", "Queue", protocol: ProtocolType.Sql);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(RuleCodes.ProtocolMismatch, finding.Code);
        Assert.Equal(Area.Resilience, finding.Area);
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.PROTOCOL_MISMATCH.sql.message(source=Orders, target=Queue)", finding.Message.Describe());
        Assert.Equal(_builder.EdgeElement("Orders", "Queue"), Assert.Single(finding.Elements));
    }

    [Fact]
    public void SqlIntoADatabase_IsWhatSqlIsFor()
    {
        _builder
            .Node("Orders", NodeType.Service)
            .Node("Db", NodeType.Database)
            .Connect("Orders", "Db", protocol: ProtocolType.Sql);

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void HttpIntoADatabase_IsOnlyAWarning()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", protocol: ProtocolType.Http);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.PROTOCOL_MISMATCH.http.message", finding.Message.Key());
    }

    [Fact]
    public void AmqpWithNoBrokerAtEitherEnd_IsAWarning()
    {
        _builder
            .Nodes(NodeType.Service, "Orders", "Billing")
            .Connect("Orders", "Billing", synchronous: false, protocol: ProtocolType.Amqp);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.PROTOCOL_MISMATCH.amqp.message", finding.Message.Key());
    }

    [Fact]
    public void AmqpIntoAQueue_IsPublishing()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Queue", NodeType.Queue)
            .Connect("Api", "Queue", synchronous: false, protocol: ProtocolType.Amqp);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void AmqpLeavingAQueue_IsTheBrokerDelivering()
    {
        _builder
            .Node("Queue", NodeType.Queue)
            .Node("Worker", NodeType.Worker)
            .Connect("Queue", "Worker", synchronous: false, protocol: ProtocolType.Amqp);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }


    [Fact]
    public void HttpIntoAQueue_IsAHostedQueueAndNotAMistake()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Queue", NodeType.Queue)
            .Connect("Api", "Queue", synchronous: false, protocol: ProtocolType.Http);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void HttpIntoACache_IsNotChecked()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cache", NodeType.Cache)
            .Connect("Api", "Cache", protocol: ProtocolType.Http);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void AnAmqpEdgeMarkedSynchronous_IsNotAFinding()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Queue", NodeType.Queue)
            .Connect("Api", "Queue", synchronous: true, timeoutSeconds: 5, protocol: ProtocolType.Amqp);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void OneFindingPerEdge_AndTheEdgeIsTheElement()
    {
        _builder
            .Nodes(NodeType.Service, "Orders", "Billing")
            .Node("Db", NodeType.Database)
            .Connect("Orders", "Db", protocol: ProtocolType.Sql)
            .Connect("Billing", "Db", protocol: ProtocolType.Http);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(_builder.EdgeElement("Billing", "Db"), Assert.Single(finding.Elements));
    }

    [Fact]
    public void AGraphWithNoEdges_IsNotApplicable()
    {
        _builder.Node("Api", NodeType.Api).Node("Db", NodeType.Database);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }


    [Fact]
    public void TheTelemetrysOwnProtocol_IsNotJudged()
    {
        _builder
            .Node("Db", NodeType.Database)
            .Node("Obs", NodeType.Observability)
            .Connect("Obs", "Db", protocol: ProtocolType.Http);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }

    [Fact]
    public void ARealMismatchBesideTheInstrumentation_IsStillReported()
    {
        _builder
            .Node("Api", NodeType.Api).Node("Db", NodeType.Database)
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Db", protocol: ProtocolType.Amqp)
            .Connect("Obs", "Db", protocol: ProtocolType.Http);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(_builder.EdgeElement("Api", "Db"), Assert.Single(finding.Elements));
    }
}
