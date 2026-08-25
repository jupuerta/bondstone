using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

public class CacheAsStoreRuleTests
{
    private readonly CacheAsStoreRule _rule = new();
    private readonly GraphBuilder _builder = new();

    [Fact]
    public void A_cache_with_nothing_behind_it_is_the_store()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cache", NodeType.Cache)
            .Connect("Api", "Cache", timeoutSeconds: 1);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(RuleCodes.CacheAsStore, finding.Code);
        Assert.Equal(Area.Resilience, finding.Area);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.CACHE_AS_STORE.noOrigin.message(node=Cache)", finding.Message.Describe());
        Assert.Equal("finding.CACHE_AS_STORE.noOrigin.explanation(node=Cache)", finding.Explanation.Describe());
        Assert.Equal("finding.CACHE_AS_STORE.noOrigin.suggestion", finding.Suggestion.Key());
        Assert.Equal([_builder.NodeElement("Cache"), _builder.EdgeElement("Api", "Cache")], finding.Elements);
        Assert.Null(finding.Fix);
    }

    [Fact]
    public void Read_through_is_a_cache()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cache", NodeType.Cache)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Cache", timeoutSeconds: 1)
            .Connect("Cache", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Cache_aside_is_a_cache()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cache", NodeType.Cache)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Cache", timeoutSeconds: 1)
            .Connect("Api", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void A_worker_warming_it_from_the_database_is_an_origin()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cache", NodeType.Cache)
            .Node("Warmer", NodeType.Worker)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Cache", timeoutSeconds: 1)
            .Connect("Warmer", "Cache", timeoutSeconds: 1)
            .Connect("Warmer", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void Write_behind_through_a_queue_is_an_origin_arriving_late()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cache", NodeType.Cache)
            .Node("Cola", NodeType.Queue)
            .Node("Worker", NodeType.Worker)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Cache", timeoutSeconds: 1)
            .Connect("Api", "Cola", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Cola", "Worker", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Worker", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void Object_storage_is_an_origin_too()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cache", NodeType.Cache)
            .Node("Files", NodeType.ObjectStorage)
            .Connect("Api", "Cache", timeoutSeconds: 1)
            .Connect("Api", "Files", timeoutSeconds: 5);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void A_database_nobody_who_uses_the_cache_can_reach_is_not_its_origin()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Cache", NodeType.Cache)
            .Node("Reports", NodeType.Worker)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Cache", timeoutSeconds: 1)
            .Connect("Reports", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
        Assert.Equal("Cache", finding.Message.Arg("node"));
    }

    [Fact]
    public void A_cache_nobody_uses_is_not_this_rules_finding()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Node("Cache", NodeType.Cache)
            .Connect("Api", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }

    [Fact]
    public void Without_a_cache_there_is_nothing_to_check()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }
}
