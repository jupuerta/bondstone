using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

/// <summary>
/// V8 §6.2. The rule that earns <c>ExternalService</c> its place in the palette, and the pair of
/// assertions that say what the type <em>means</em>: you cannot make it bigger, and the only thing
/// left is not needing it to answer.
/// </summary>
public class ExternalNoFallbackRuleTests
{
    private static ArchitectureGraph Calling(int? timeoutSeconds = 5, bool circuitBreaker = false, bool fallback = false, bool synchronous = true) =>
        new GraphBuilder()
            .Node("Checkout", NodeType.Service)
            .Node("Provider", NodeType.ExternalService)
            .Connect("Checkout", "Provider", synchronous: synchronous, timeoutSeconds: timeoutSeconds,
                circuitBreaker: circuitBreaker, fallback: fallback)
            .Build();

    [Fact]
    public void A_timeout_is_not_enough_on_somebody_elses_service()
    {
        // This is the whole difference with MISSING_RESILIENCE, which a timeout does silence: a
        // bounded wait still leaves the caller with no answer, and here there is no third option.
        var result = new ExternalNoFallbackRule().Evaluate(Calling());

        Assert.True(result.IsApplicable);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.EXTERNAL_NO_FALLBACK.unprotected.message(source=Checkout, target=Provider)", finding.Message.Describe());
        Assert.NotNull(finding.Fix);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Either_answering_without_it_or_stopping_to_ask_clears_it(bool circuitBreaker, bool fallback)
    {
        Assert.Empty(new ExternalNoFallbackRule().Evaluate(Calling(circuitBreaker: circuitBreaker, fallback: fallback)).Findings);
    }

    [Fact]
    public void An_asynchronous_call_is_not_waiting_for_anybody()
    {
        Assert.Empty(new ExternalNoFallbackRule().Evaluate(Calling(synchronous: false, timeoutSeconds: null)).Findings);
    }

    [Fact]
    public void Nothing_to_say_when_nobody_depends_on_a_third_party()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", protocol: ProtocolType.Sql)
            .Build();

        Assert.False(new ExternalNoFallbackRule().Evaluate(graph).IsApplicable);
    }
}

/// <summary>V8 §6.2. What the type is, as the two models see it.</summary>
public class ExternalServiceCapacityTests
{
    [Fact]
    public void Nothing_anybody_configures_makes_a_third_party_bigger()
    {
        var node = new Domain.Entities.Node
        {
            Id = Guid.NewGuid(),
            Name = "Provider",
            Type = NodeType.ExternalService,
            Configuration = """{"instances":8,"replicas":8,"readReplicas":8}"""
        };

        Assert.Equal(1, NodeSettings.Instances(node));
        Assert.Equal(CapacityModel.ExternalService, CapacityModel.PerInstance(NodeType.ExternalService));
    }

    [Fact]
    public void The_rule_that_would_tell_you_to_scale_it_looks_away()
    {
        // Its suggestions are all "add instances", "add replicas", "share the work" - three things
        // that cannot be done to somebody else's service. A finding with an impossible suggestion
        // is worse than no finding.
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":900}""")
            .Node("Provider", NodeType.ExternalService)
            .Connect("Api", "Provider", timeoutSeconds: 5, fallback: true)
            .Build();

        var findings = new UndersizedComponentRule().Evaluate(graph).Findings;

        Assert.Equal(900m / CapacityModel.ExternalService, LoadModel.Of(graph).UtilizationOf(graph.Nodes.Single(n => n.Name == "Provider").Id));
        Assert.DoesNotContain(findings, f => f.Elements.Any(e => e.Id == graph.Nodes.Single(n => n.Name == "Provider").Id));
    }
}
