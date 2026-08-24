using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

public class MissingCacheRuleTests
{
    private readonly MissingCacheRule _rule = new();
    private readonly GraphBuilder _builder = new();

    [Fact]
    public void ThreeDirectConsumersWithoutCacheOrReplicas_IsAWarning()
    {
        _builder
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Service, "Api", "Service", "Worker")
            .Connect("Api", "Db").Connect("Service", "Db").Connect("Worker", "Db");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(RuleCodes.MissingCache, finding.Code);
        Assert.Equal(Area.Scalability, finding.Area);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.MISSING_CACHE.direct.message(count=3, node=Db)", finding.Message.Describe());
    }

    [Fact]
    public void SingleConsumer_Passes()
    {
        // The permissive version of this rule would flag Api -> Database, the most common correct
        // small architecture there is (D4.3).
        _builder
            .Node("Db", NodeType.Database)
            .Node("Api", NodeType.Api)
            .Connect("Api", "Db");

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void ReadReplicasDeclared_Passes()
    {
        _builder
            .Node("Db", NodeType.Database, configuration: """{"readReplicas":2}""")
            .Nodes(NodeType.Service, "Api", "Service")
            .Connect("Api", "Db").Connect("Service", "Db");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void ReadReplicasSetToZero_StillFires()
    {
        // Declaring zero replicas is declaring there are none.
        _builder
            .Node("Db", NodeType.Database, configuration: """{"readReplicas":0}""")
            .Nodes(NodeType.Service, "Api", "Service")
            .Connect("Api", "Db").Connect("Service", "Db");

        Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void CacheInFrontOfTheDatabase_IsNotItsOwnConsumer()
    {
        // Without this precision Api -> Cache -> Database would count the cache as a consumer and
        // the "no cache interposed" condition would contradict itself (D4.3).
        _builder
            .Node("Db", NodeType.Database)
            .Node("Cache", NodeType.Cache)
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Connect("Api1", "Cache").Connect("Api2", "Cache")
            .Connect("Cache", "Db");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void ConsumerWithACacheOnItsWay_IsNotEffective()
    {
        // Orders reaches the database through a cache, so it is not adding direct read load.
        _builder
            .Node("Db", NodeType.Database)
            .Node("Cache", NodeType.Cache)
            .Nodes(NodeType.Service, "Orders", "Users")
            .Connect("Orders", "Cache").Connect("Cache", "Db")
            .Connect("Users", "Db");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void AsynchronousConsumersDoNotCount()
    {
        _builder
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Service, "Api", "Worker")
            .Connect("Api", "Db")
            .Connect("Worker", "Db", synchronous: false);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void Finding_PointsAtTheDatabaseAndItsIncomingEdges()
    {
        _builder
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Service, "Api", "Service")
            .Connect("Api", "Db").Connect("Service", "Db");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(["Db", "Api->Db", "Service->Db"], _builder.Names(finding.Elements));
    }

    // ---- V14 B7: components, not arrows ----------------------------------------

    [Fact]
    public void OneConsumerDrawnTwice_IsOneConsumer()
    {
        // QA V14 T4: a single Api with two arrows to the same database said "2 componentes". The
        // threshold is called MinConsumers; it was being fed the length of an edge list.
        _builder
            .Node("Db", NodeType.Database)
            .Node("Api", NodeType.Api)
            .Connect("Api", "Db").Connect("Api", "Db");

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void TheCardCountsConsumers_AndTheCanvasGetsEveryArrow()
    {
        // Two consumers, three arrows (QA V14 C8 said "3 componentes" here). The count the card
        // prints is the one HIGH_COUPLING would make - "depending twice on the same thing is one
        // dependency" - and every arrow still travels in Elements, because two arrows are two
        // things to light up even when they are one consumer.
        _builder
            .Node("Db", NodeType.Database)
            .Node("Api", NodeType.Api)
            .Node("Service", NodeType.Service)
            .Connect("Api", "Db").Connect("Api", "Db").Connect("Service", "Db");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.MISSING_CACHE.direct.message(count=2, node=Db)", finding.Message.Describe());
        Assert.Equal(["Db", "Api->Db", "Api->Db", "Service->Db"], _builder.Names(finding.Elements));
    }

    [Fact]
    public void NeverEscalatesToCritical()
    {
        // A performance recommendation, not a failure: treating it as critical would devalue the
        // real criticals (D4.3).
        _builder.Node("Db", NodeType.Database);

        for (var i = 0; i < 10; i++)
        {
            _builder.Node($"S{i}");
            _builder.Connect($"S{i}", "Db");
        }

        Assert.Equal(Severity.Warning, Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Severity);
    }

    [Fact]
    public void NoDatabaseWithConsumers_IsNotApplicable()
    {
        _builder
            .Node("Db", NodeType.Database)
            .Node("Api", NodeType.Api);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }

    // ---- V15 B2: two replicas are one consumer ----------------------------------

    [Fact]
    public void TwoReplicasBehindABalancer_AreOneConsumer()
    {
        // QA V15 C4 (T3): `LB -> Pedidos1, Pedidos2 -> Db` said "'Db' recibe consultas directas de 2
        // componentes". Boxes of the same type fanned out from one LoadBalancer are the one kind of
        // copy the model recognises, and B7's "components, not arrows" extends to them: a copy is
        // not another consumer.
        _builder
            .Node("Db", NodeType.Database)
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Service, "Pedidos1", "Pedidos2")
            .Connect("LB", "Pedidos1").Connect("LB", "Pedidos2")
            .Connect("Pedidos1", "Db").Connect("Pedidos2", "Db");

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void TwoServicesBehindAGateway_AreStillTwoConsumers()
    {
        // The V15 trap: a Gateway is a router too, and grouping by IsRouter would take
        // MISSING_CACHE off gateway-microservices and worst-case, whose services hang from one.
        _builder
            .Node("Db", NodeType.Database)
            .Node("Gw", NodeType.Gateway)
            .Nodes(NodeType.Service, "Users", "Orders")
            .Connect("Gw", "Users").Connect("Gw", "Orders")
            .Connect("Users", "Db").Connect("Orders", "Db");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.MISSING_CACHE.direct.message(count=2, node=Db)", finding.Message.Describe());
    }

    [Fact]
    public void AReplicaSetBesideAnotherConsumer_CountsOnce_AndTheCanvasGetsEveryArrow()
    {
        // Two replicas of Api and a separate Service: two consumers, and all three arrows lit -
        // B7's split between what the card counts and what the canvas shows.
        _builder
            .Node("Db", NodeType.Database)
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Node("Svc", NodeType.Service)
            .Connect("LB", "Api1").Connect("LB", "Api2")
            .Connect("Api1", "Db").Connect("Api2", "Db").Connect("Svc", "Db");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.MISSING_CACHE.direct.message(count=2, node=Db)", finding.Message.Describe());
        Assert.Equal(["Db", "Api1->Db", "Api2->Db", "Svc->Db"], _builder.Names(finding.Elements));
    }

    [Fact]
    public void ABalancerOverOneBox_MakesNoCopy()
    {
        // Negative control on the grouping: a single target behind the balancer is not a replica
        // set, so Users and Orders stay two consumers.
        _builder
            .Node("Db", NodeType.Database)
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Service, "Users", "Orders")
            .Connect("LB", "Users")
            .Connect("Users", "Db").Connect("Orders", "Db");

        Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
    }
}
