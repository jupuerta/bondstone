using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

public class SpofRuleZonesTests
{
    private readonly SpofRule _rule = new();

    [Fact]
    public void NobodyDeclaresAZone_AndTheAnswerIsTheOneItAlwaysWas()
    {
        Assert.Empty(_rule.Evaluate(Fleet(null, null).Build()).Findings);
    }

    [Fact]
    public void TwoCopiesSharingAZone_AreASinglePointOfFailureThatOnlyWarns()
    {
        var builder = Fleet("a", "a");

        var finding = Assert.Single(_rule.Evaluate(builder.Build()).Findings);

        Assert.Equal(RuleCodes.Spof, finding.Code);
        Assert.Equal(Area.Availability, finding.Area);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(
            "finding.SPOF.sameZone.message(count=2, names='Api1', 'Api2', zone=a)",
            finding.Message.Describe());
        Assert.Equal("finding.SPOF.sameZone.explanation(zone=a)", finding.Explanation.Describe());
        Assert.Equal("finding.SPOF.suggestion.sameZone", finding.Suggestion.Describe());
        Assert.Equal(
            [builder.NodeElement("Api1"), builder.NodeElement("Api2")],
            finding.Elements);
    }

    [Fact]
    public void TwoCopiesInTwoZones_AreExemptLikeTheyAlwaysWere()
    {
        Assert.Empty(_rule.Evaluate(Fleet("a", "b").Build()).Findings);
    }

    // Until 30-08 one copy without a zone exempted the whole set, so hiding the datum on a single
    // box scored better than telling the truth (QA §1.5, §4.3). «I do not know where it is» is not
    // «it is somewhere else»: the one zone anybody declared is the zone they are all in.
    [Theory]
    [InlineData("a", null)]
    [InlineData(null, "c")]
    public void OneCopyThatNeverSaidWhereItIs_IsNotACopySomewhereElse(string? first, string? second)
    {
        var finding = Assert.Single(_rule.Evaluate(Fleet(first, second).Build()).Findings);

        Assert.Equal("finding.SPOF.sameZone.message", finding.Message.Key());
        Assert.Equal(first ?? second, finding.Message.Arg("zone"));
        Assert.Equal(2, finding.Elements.Count);
    }

    [Fact]
    public void NobodySayingWhereTheyAre_IsStillTheOptIn()
    {
        Assert.Empty(_rule.Evaluate(Fleet(null, null).Build()).Findings);
    }

    [Theory]
    [InlineData("z")]
    [InlineData("A")]
    [InlineData("")]
    public void AZoneOutsideTheClosedSet_IsNoZoneAtAll(string zone)
    {
        Assert.Empty(_rule.Evaluate(Fleet(zone, zone).Build()).Findings);
    }

    [Fact]
    public void MovingOneCopyOut_IsTheWayThroughTheFinding()
    {
        Assert.Single(_rule.Evaluate(Fleet("a", "a").Build()).Findings);
        Assert.Empty(_rule.Evaluate(Fleet("a", "b").Build()).Findings);
    }

    [Fact]
    public void TheSetIsReportedOnce_CarryingEveryCopyAsAnElement()
    {
        var builder = new GraphBuilder()
            .Node("Users", NodeType.Client)
            .Node("LB", NodeType.LoadBalancer)
            .Node("Api1", NodeType.Api, Placed("a"))
            .Node("Api2", NodeType.Api, Placed("a"))
            .Node("Api3", NodeType.Api, Placed("a"))
            .Node("Db", NodeType.Database, """{"replicas": 2}""")
            .Node("Queue", NodeType.Queue)
            .Connect("Users", "LB")
            .Connect("LB", "Api1").Connect("LB", "Api2").Connect("LB", "Api3")
            .Connect("Api1", "Db").Connect("Api2", "Db").Connect("Api3", "Db")
            .Connect("Api1", "Queue", synchronous: false)
            .Connect("Api2", "Queue", synchronous: false)
            .Connect("Api3", "Queue", synchronous: false);

        var finding = Assert.Single(_rule.Evaluate(builder.Build()).Findings);

        Assert.Equal("3", finding.Message.Arg("count"));
        Assert.Equal("'Api1', 'Api2', 'Api3'", finding.Message.Arg("names"));
        Assert.Equal(3, finding.Elements.Count);
    }

    [Fact]
    public void TheCopiesOfOneBox_LiveWhereTheBoxLives()
    {
        var builder = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"instances": 2, "zone": "b"}""")
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Api").Connect("Payments", "Api");

        var finding = Assert.Single(_rule.Evaluate(builder.Build()).Findings);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(
            "finding.SPOF.sameZone.message(count=1, names='Api', zone=b)",
            finding.Message.Describe());
        Assert.Equal(builder.NodeElement("Api"), Assert.Single(finding.Elements));
    }

    [Fact]
    public void AStoreThatReplicatesInside_IsNeverAskedWhereItsReplicasAre()
    {
        var replicated = new GraphBuilder()
            .Node("Db", NodeType.Database, """{"replicas": 2, "zone": "a"}""")
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Db").Connect("Payments", "Db");

        Assert.Empty(_rule.Evaluate(replicated.Build()).Findings);

        var unplaced = new GraphBuilder()
            .Node("Db", NodeType.Database, """{"replicas": 2}""")
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Db").Connect("Payments", "Db");

        Assert.Empty(_rule.Evaluate(unplaced.Build()).Findings);
    }

    [Fact]
    public void TwoStoresTheDrawingCanPlace_AreAskedWhereTheyAre()
    {
        var together = Assert.Single(_rule.Evaluate(Stores("a", "a").Build()).Findings);

        Assert.Equal(Severity.Warning, together.Severity);
        Assert.Equal("finding.SPOF.sameZone.message", together.Message.Key());
        Assert.Equal("'Db1', 'Db2'", together.Message.Arg("names"));

        Assert.Empty(_rule.Evaluate(Stores("a", "c").Build()).Findings);
    }

    [Fact]
    public void TheSetIsGradedAsOneBox_NotAsSeveralSeparateOnes()
    {
        var builder = new GraphBuilder()
            .Node("Users", NodeType.Client)
            .Node("LB", NodeType.LoadBalancer)
            .Node("Api1", NodeType.Api, Placed("a"))
            .Node("Api2", NodeType.Api, Placed("a"))
            .Node("Db", NodeType.Database, """{"replicas": 2}""")
            .Connect("Users", "LB")
            .Connect("LB", "Api1").Connect("LB", "Api2")
            .Connect("Api1", "Db").Connect("Api2", "Db");

        Assert.Empty(_rule.Evaluate(builder.Build()).Findings);
    }

    [Fact]
    public void TheZoneNeverEscalatesToCritical()
    {
        var builder = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"instances": 4, "zone": "a"}""")
            .Nodes(NodeType.Service, "Orders", "Payments", "Users", "Billing")
            .Connect("Orders", "Api").Connect("Payments", "Api")
            .Connect("Users", "Api").Connect("Billing", "Api");

        Assert.Equal(Severity.Warning, Assert.Single(_rule.Evaluate(builder.Build()).Findings).Severity);
    }

    [Fact]
    public void ItPublishesOneWarningFactPerCopy_SoNothingCascadesOffIt()
    {
        var graph = Fleet("a", "a").Build();

        _rule.Evaluate(graph);

        var facts = graph.Facts.OfType<SinglePointOfFailureFact>().ToList();

        Assert.Equal(2, facts.Count);
        Assert.All(facts, fact => Assert.Equal(Severity.Warning, fact.Severity));
    }

    [Fact]
    public void TheBalancerInFrontOfThem_IsNotGradedByItsOwnZone()
    {
        var builder = Fleet("a", "a", balancer: "a");

        var finding = Assert.Single(_rule.Evaluate(builder.Build()).Findings);

        Assert.DoesNotContain(builder.NodeElement("LB"), finding.Elements);
    }

    [Fact]
    public void AZoneOnSomethingWithNoRedundancy_ChangesNothingAboutIt()
    {
        var builder = new GraphBuilder()
            .Node("Db", NodeType.Database, Placed("a"))
            .Nodes(NodeType.Service, "Orders", "Payments", "Users")
            .Connect("Orders", "Db").Connect("Payments", "Db").Connect("Users", "Db");

        var finding = Assert.Single(_rule.Evaluate(builder.Build()).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.SPOF.shared.message(count=3, node=Db)", finding.Message.Describe());
        Assert.Equal("finding.SPOF.suggestion.database", finding.Suggestion.Describe());
    }

    private static GraphBuilder Fleet(string? first, string? second, string? balancer = null) =>
        new GraphBuilder()
            .Node("Users", NodeType.Client)
            .Node("LB", NodeType.LoadBalancer, Placed(balancer))
            .Node("Api1", NodeType.Api, Placed(first))
            .Node("Api2", NodeType.Api, Placed(second))
            .Node("Db", NodeType.Database, """{"replicas": 2}""")
            .Node("Queue", NodeType.Queue)
            .Connect("Users", "LB")
            .Connect("LB", "Api1").Connect("LB", "Api2")
            .Connect("Api1", "Db").Connect("Api2", "Db")
            .Connect("Api1", "Queue", synchronous: false)
            .Connect("Api2", "Queue", synchronous: false);

    private static GraphBuilder Stores(string first, string second) =>
        new GraphBuilder()
            .Node("LB", NodeType.LoadBalancer)
            .Node("Db1", NodeType.Database, $$"""{"replicas": 2, "zone": "{{first}}"}""")
            .Node("Db2", NodeType.Database, $$"""{"replicas": 2, "zone": "{{second}}"}""")
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("LB", "Db1").Connect("LB", "Db2")
            .Connect("Orders", "Db1").Connect("Orders", "Db2")
            .Connect("Payments", "Db1").Connect("Payments", "Db2");

    private static string Placed(string? zone) => zone is null ? "{}" : $$"""{"zone": "{{zone}}"}""";
}
