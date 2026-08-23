using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

/// <summary>
/// V13 D3.1. Half of these tests are about what the rule <strong>does not</strong> say, and that is
/// the point: a coherence rule that fires on a legitimate drawing is worse than no coherence rule,
/// because it teaches people to stop reading the panel.
/// </summary>
public class ProtocolMismatchRuleTests
{
    private readonly ProtocolMismatchRule _rule = new();
    private readonly GraphBuilder _builder = new();

    [Fact]
    public void SqlIntoSomethingThatIsNotADatabase_IsCritical()
    {
        // The example the review opened with. There is no architecture in which this line is what
        // its author meant, so it is the one Critical of the three.
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
        // The one of the three with an innocent reading - a document store, a search engine, a REST
        // layer somebody did not draw - so it asks rather than asserts.
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
        // The shape this analyzer spends its time recommending. Reading only the destination would
        // have flagged the second half of every asynchronous pipeline in the catalogue.
        _builder
            .Node("Queue", NodeType.Queue)
            .Node("Worker", NodeType.Worker)
            .Connect("Queue", "Worker", synchronous: false, protocol: ProtocolType.Amqp);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    // ---- What it deliberately allows (D3.1) -----------------------------------

    [Fact]
    public void HttpIntoAQueue_IsAHostedQueueAndNotAMistake()
    {
        // A queue with an HTTP API is the most ordinary thing there is. Being strict here would be
        // a false positive on a real and common design.
        _builder
            .Node("Api", NodeType.Api)
            .Node("Queue", NodeType.Queue)
            .Connect("Api", "Queue", synchronous: false, protocol: ProtocolType.Http);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void HttpIntoACache_IsNotChecked()
    {
        // The model has three protocols and Http is the general one. Demanding a fourth for every
        // store would fire on half the diagrams to say nothing.
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cache", NodeType.Cache)
            .Connect("Api", "Cache", protocol: ProtocolType.Http);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void AnAmqpEdgeMarkedSynchronous_IsNotAFinding()
    {
        // **The case the review asked for and this rule refuses to make.** The manual says where it
        // explains the field that "RPC sobre una cola" exists and is why synchrony is marked by
        // hand, so request/reply over a broker is a pattern the product teaches. A rule forbidding
        // it would contradict its own documentation to flag a legitimate design.
        _builder
            .Node("Api", NodeType.Api)
            .Node("Queue", NodeType.Queue)
            .Connect("Api", "Queue", synchronous: true, timeoutSeconds: 5, protocol: ProtocolType.Amqp);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void OneFindingPerEdge_AndTheEdgeIsTheElement()
    {
        // Granularity (D4.5): the subject is the call, never the component at either end - the same
        // Database can be reached correctly by one edge and wrongly by another.
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

    // ---- V14 B9: the protocol of a scrape is not the architect's claim -------

    [Fact]
    public void TheTelemetrysOwnProtocol_IsNotJudged()
    {
        // T1b/T1c. `Obs -> Db` over Http was told to consider Sql: advice to make the metrics
        // backend speak the database's wire protocol, about an arrow that is not a call in the
        // architecture's sense.
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
