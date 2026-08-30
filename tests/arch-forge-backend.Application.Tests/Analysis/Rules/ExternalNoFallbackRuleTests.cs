using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

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
        var result = new ExternalNoFallbackRule().Evaluate(Calling());

        Assert.True(result.IsApplicable);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.EXTERNAL_NO_FALLBACK.unprotected.message(source=Checkout, target=Provider)", finding.Message.Describe());
        Assert.Equal("finding.EXTERNAL_NO_FALLBACK.unprotected.explanation", finding.Explanation.Key());
        Assert.Equal("finding.EXTERNAL_NO_FALLBACK.unprotected.suggestion", finding.Suggestion.Key());

        Assert.NotNull(finding.Fix);
        Assert.Null(finding.Fix!.TimeoutSeconds);
        Assert.True(finding.Fix.Fallback);
        Assert.Equal("fix.EXTERNAL_NO_FALLBACK.addFallback", finding.Fix.Label.Key());
    }

    [Fact]
    public void Without_a_timeout_the_finding_says_so_and_the_fix_sets_both()
    {
        var finding = Assert.Single(new ExternalNoFallbackRule().Evaluate(Calling(timeoutSeconds: null)).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.EXTERNAL_NO_FALLBACK.unprotected.message(source=Checkout, target=Provider)", finding.Message.Describe());
        Assert.Equal("finding.EXTERNAL_NO_FALLBACK.noTimeout.explanation(source=Checkout, target=Provider)", finding.Explanation.Describe());
        Assert.Equal("finding.EXTERNAL_NO_FALLBACK.noTimeout.suggestion", finding.Suggestion.Key());

        Assert.NotNull(finding.Fix);
        Assert.Equal(FixKinds.SetEdgeResilience, finding.Fix!.Kind);
        Assert.Equal(5, finding.Fix.TimeoutSeconds);
        Assert.True(finding.Fix.Fallback);
        Assert.Equal("fix.EXTERNAL_NO_FALLBACK.timeoutAndFallback(seconds=5)", finding.Fix.Label.Describe());
    }

    [Fact]
    public void It_tells_MISSING_RESILIENCE_which_calls_it_has_already_covered()
    {
        var rule = new ExternalNoFallbackRule();
        Assert.Equal(RulePhase.Producer, rule.Phase);

        var reported = Calling(timeoutSeconds: null);
        rule.Evaluate(reported);
        var fact = Assert.Single(reported.Facts.OfType<ExternalNoFallbackFact>());
        Assert.Equal(reported.Connections.Single().Id, fact.ConnectionId);

        var contained = Calling(timeoutSeconds: null, fallback: true);
        rule.Evaluate(contained);
        Assert.Empty(contained.Facts);
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
            .Connect("Api", "Db")
            .Build();

        Assert.False(new ExternalNoFallbackRule().Evaluate(graph).IsApplicable);
    }
}

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
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":900}""")
            .Node("Provider", NodeType.ExternalService)
            .Connect("Api", "Provider", timeoutSeconds: 5, fallback: true)
            .Build();

        var findings = new UndersizedComponentRule().Evaluate(graph).Findings;

        Assert.Equal(900m / CapacityModel.ExternalService, LoadModel.Of(graph).UtilizationOf(graph.Nodes.Single(n => n.Name == "Provider").Id));
        Assert.DoesNotContain(findings, f => f.Elements.Any(e => e.Id == graph.Nodes.Single(n => n.Name == "Provider").Id));
    }


    [Fact]
    public void A_third_partys_call_to_another_third_party_is_not_yours_to_protect()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Stripe", NodeType.ExternalService)
            .Node("Maps", NodeType.ExternalService)
            .Connect("Api", "Stripe", timeoutSeconds: 2, fallback: true)
            .Connect("Stripe", "Maps")
            .Build();

        var result = new ExternalNoFallbackRule().Evaluate(graph);

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Your_own_unprotected_call_to_the_provider_is_still_the_finding()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Stripe", NodeType.ExternalService)
            .Node("Maps", NodeType.ExternalService)
            .Connect("Api", "Stripe")
            .Connect("Stripe", "Maps")
            .Build();

        var finding = Assert.Single(new ExternalNoFallbackRule().Evaluate(graph).Findings);

        Assert.Equal(FindingElement.Connection(graph.Connections.Single(c => c.SourceNodeId == graph.Nodes.Single(n => n.Name == "Api").Id).Id), Assert.Single(finding.Elements));
    }

    [Fact]
    public void The_telemetry_box_calling_a_provider_is_the_B9_exception_not_B10()
    {
        var graph = new GraphBuilder()
            .Node("Obs", NodeType.Observability)
            .Node("Stripe", NodeType.ExternalService)
            .Connect("Obs", "Stripe")
            .Build();

        Assert.Single(new ExternalNoFallbackRule().Evaluate(graph).Findings);
    }
}
