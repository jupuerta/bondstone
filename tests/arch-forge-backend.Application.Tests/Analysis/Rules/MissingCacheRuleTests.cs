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
    public void ThreeDirectConsumersWithoutCacheOrReplicas_IsWorthSaying()
    {
        _builder
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Service, "Api", "Service", "Worker")
            .Connect("Api", "Db").Connect("Service", "Db").Connect("Worker", "Db");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(RuleCodes.MissingCache, finding.Code);
        Assert.Equal(Area.Scalability, finding.Area);
        // V20. `Info` y no `Warning`: es la única regla del analizador que señala una oportunidad
        // y no un defecto — todo lo que sugiere sigue funcionando —, y compartir nivel con
        // `QUEUE_NO_DLQ` ponía «esto podría ir más rápido» al lado de «estás perdiendo mensajes».
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("finding.MISSING_CACHE.direct.message(count=3, node=Db)", finding.Message.Describe());
    }

    [Fact]
    public void SingleConsumer_Passes()
    {
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
        _builder
            .Node("Db", NodeType.Database, configuration: """{"readReplicas":0}""")
            .Nodes(NodeType.Service, "Api", "Service")
            .Connect("Api", "Db").Connect("Service", "Db");

        Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void CacheInFrontOfTheDatabase_IsNotItsOwnConsumer()
    {
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


    [Fact]
    public void OneConsumerDrawnTwice_IsOneConsumer()
    {
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
    public void NeverEscalatesAboveInfo()
    {
        _builder.Node("Db", NodeType.Database);

        for (var i = 0; i < 10; i++)
        {
            _builder.Node($"S{i}");
            _builder.Connect($"S{i}", "Db");
        }

        Assert.Equal(Severity.Info, Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Severity);
    }

    [Fact]
    public void NoDatabaseWithConsumers_IsNotApplicable()
    {
        _builder
            .Node("Db", NodeType.Database)
            .Node("Api", NodeType.Api);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }


    [Fact]
    public void TwoReplicasBehindABalancer_AreOneConsumer()
    {
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
        _builder
            .Node("Db", NodeType.Database)
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Service, "Users", "Orders")
            .Connect("LB", "Users")
            .Connect("Users", "Db").Connect("Orders", "Db");

        Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
    }
}
