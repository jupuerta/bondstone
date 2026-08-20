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
}
