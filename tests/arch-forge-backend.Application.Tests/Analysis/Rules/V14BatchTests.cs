using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

public class ObservabilityFloorTests
{
    private readonly NoObservabilityRule _rule = new();

    [Fact]
    public void TotalBlindness_OnARealDiagram_IsOneCriticalAndAWarningPerBox()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Svc", NodeType.Service).Node("Db", NodeType.Database)
            .Connect("Api", "Svc").Connect("Svc", "Db", protocol: ProtocolType.Sql)
            .Build();

        var findings = _rule.Evaluate(graph).Findings;

        var blind = Assert.Single(findings, f => f.Severity == Severity.Critical);
        Assert.Equal("finding.NO_OBSERVABILITY.blind.message(count=3)", blind.Message.Describe());
        Assert.Equal("finding.NO_OBSERVABILITY.blind.suggestion.absent", blind.Suggestion.Key());
        Assert.Null(blind.Fix);
        Assert.Equal(3, blind.Elements.Count);

        Assert.Equal(3, findings.Count(f => f.Severity == Severity.Warning));
        Assert.All(findings.Where(f => f.Severity == Severity.Warning),
            f => Assert.Equal("finding.NO_OBSERVABILITY.unobserved.message", f.Message.Key()));
    }

    [Fact]
    public void TheColourOfTheArea_DoesNotDependOnCountingBoxes()
    {
        static GraphBuilder Behind(params string[] apis)
        {
            var b = new GraphBuilder().Node("Web", NodeType.Client).Node("LB", NodeType.LoadBalancer)
                .Nodes(NodeType.Api, apis).Node("Db", NodeType.Database)
                .Connect("Web", "LB");
            foreach (var api in apis)
                b.Connect("LB", api, timeoutSeconds: 3, fallback: true).Connect(api, "Db", timeoutSeconds: 3, fallback: true, protocol: ProtocolType.Sql);
            return b;
        }

        var one = _rule.Evaluate(Behind("Api").Build()).Findings;
        var two = _rule.Evaluate(Behind("Api1", "Api2").Build()).Findings;

        Assert.Single(one, f => f.Severity == Severity.Critical);
        Assert.Single(two, f => f.Severity == Severity.Critical);

        Assert.Equal(3, one.Count(f => f.Severity == Severity.Warning));
        Assert.Equal(4, two.Count(f => f.Severity == Severity.Warning));
    }

    [Fact]
    public void OneWire_LiftsTheBlindnessAndLeavesTheHomework()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Svc", NodeType.Service).Node("Db", NodeType.Database)
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Svc").Connect("Svc", "Db", protocol: ProtocolType.Sql)
            .Connect("Api", "Obs", synchronous: false)
            .Build();

        var findings = _rule.Evaluate(graph).Findings;

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(Severity.Warning, f.Severity));
    }

    [Fact]
    public void ATelemetryBoxNothingReaches_DoesNotLiftTheBlindness()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Svc", NodeType.Service).Node("Db", NodeType.Database)
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Svc").Connect("Svc", "Db", protocol: ProtocolType.Sql)
            .Build();

        var findings = _rule.Evaluate(graph).Findings;

        var blind = Assert.Single(findings, f => f.Severity == Severity.Critical);
        Assert.Equal("finding.NO_OBSERVABILITY.blind.suggestion.present(telemetry=Obs)", blind.Suggestion.Describe());
        Assert.Equal(3, findings.Count(f => f.Severity == Severity.Warning));
    }

    [Fact]
    public void TwoBoxes_AreASketchAndKeepTheWarning()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Db", NodeType.Database)
            .Connect("Api", "Db", protocol: ProtocolType.Sql)
            .Build();

        var findings = _rule.Evaluate(graph).Findings;

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(Severity.Warning, f.Severity));
    }

    [Fact]
    public void WhatTotalBlindnessCosts_GrowsWithTheSystem()
    {
        static int Blind(int services)
        {
            var b = new GraphBuilder().Node("Gw", NodeType.Gateway);
            for (var i = 1; i < services; i++)
                b.Node($"S{i}", NodeType.Service).Connect("Gw", $"S{i}", timeoutSeconds: 3, fallback: true);
            return ReferenceArchitecture.Analyze(b.Build()).ScoreOf(Area.Observability)!.Value;
        }

        Assert.Equal(78, Blind(3));
        Assert.Equal(60, Blind(10));
        Assert.Equal(55, Blind(12));
        Assert.Equal(55, Blind(20));
    }

    [Fact]
    public void TheIdentityProvider_IsNotYoursToWatch()
    {
        var graph = new GraphBuilder()
            .Node("Gw", NodeType.Gateway).Node("Auth", NodeType.IdentityProvider).Node("Db", NodeType.Database)
            .Node("Obs", NodeType.Observability)
            .Connect("Gw", "Auth", timeoutSeconds: 2, fallback: true)
            .Connect("Gw", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Gw", "Obs", synchronous: false).Connect("Db", "Obs", synchronous: false)
            .Build();

        Assert.Empty(new NoObservabilityRule().Evaluate(graph).Findings);
        Assert.False(NodeRoles.IsYoursToSize(NodeType.IdentityProvider));
    }
}

public class TrafficRejectedRuleTests
{
    private readonly TrafficRejectedRule _rule = new();

    private static ArchitectureGraph Door(int declared, int limit) => new GraphBuilder()
        .Node("Web", NodeType.Client, $$"""{"requestsPerSecond":{{declared}}}""")
        .Node("Gw", NodeType.Gateway, $$"""{"rateLimit":{{limit}}}""")
        .Node("Svc", NodeType.Service, """{"instances":10}""")
        .Connect("Web", "Gw").Connect("Gw", "Svc", timeoutSeconds: 5)
        .Build();

    [Fact]
    public void TurningAwayNearlyEveryone_IsCritical()
    {
        var finding = Assert.Single(_rule.Evaluate(Door(1_000_000, 100)).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal(Area.Availability, finding.Area);
        Assert.Equal("finding.TRAFFIC_REJECTED.shedding.message", finding.Message.Key());
        Assert.Equal("1000000", finding.Explanation.Arg("demandRps"));
        Assert.Equal("100", finding.Explanation.Arg("admittedRps"));
    }

    [Fact]
    public void StandingInForCapacity_IsAWarning()
    {
        Assert.Equal(Severity.Warning, Assert.Single(_rule.Evaluate(Door(1_000, 300)).Findings).Severity);
    }

    [Fact]
    public void SheddingAPeak_IsTheLimitDoingItsJob()
    {
        var result = _rule.Evaluate(Door(500, 300));

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void WithoutALimitOrWithoutLoad_ThereIsNothingToCheck()
    {
        var unlimited = new GraphBuilder()
            .Node("Web", NodeType.Client, """{"requestsPerSecond":1000000}""")
            .Node("Gw", NodeType.Gateway)
            .Connect("Web", "Gw")
            .Build();

        Assert.False(_rule.Evaluate(unlimited).IsApplicable);

        var idle = new GraphBuilder()
            .Node("Gw", NodeType.Gateway, """{"rateLimit":100}""")
            .Node("Svc", NodeType.Service)
            .Connect("Gw", "Svc")
            .Build();

        Assert.False(_rule.Evaluate(idle).IsApplicable);
    }
}

public class MissingCdnRuleTests
{
    private readonly MissingCdnRule _rule = new();

    [Fact]
    public void ABusyDoorServingDirectly_IsAsked()
    {
        var graph = new GraphBuilder()
            .Node("Web", NodeType.Client, """{"requestsPerSecond":2000}""")
            .Node("Gw", NodeType.Gateway)
            .Node("Svc", NodeType.Service, """{"instances":10}""")
            .Connect("Web", "Gw").Connect("Gw", "Svc", timeoutSeconds: 5)
            .Build();

        var finding = Assert.Single(_rule.Evaluate(graph).Findings);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.MISSING_CDN.direct.message", finding.Message.Key());
        Assert.Equal("2000", finding.Message.Arg("loadRps"));
    }

    [Fact]
    public void ACdnInFront_IsTheAnswer_TwiceOver()
    {
        var absorbing = new GraphBuilder()
            .Node("Web", NodeType.Client, """{"requestsPerSecond":2000}""")
            .Node("Edge", NodeType.Cdn)
            .Node("Gw", NodeType.Gateway)
            .Connect("Web", "Edge").Connect("Edge", "Gw")
            .Build();

        Assert.False(_rule.Evaluate(absorbing).IsApplicable);

        var missing = new GraphBuilder()
            .Node("Web", NodeType.Client, """{"requestsPerSecond":2000}""")
            .Node("Edge", NodeType.Cdn, """{"hitRatio":0}""")
            .Node("Gw", NodeType.Gateway)
            .Connect("Web", "Edge").Connect("Edge", "Gw")
            .Build();

        var result = _rule.Evaluate(missing);

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void ModestTraffic_IsNobodysScolding()
    {
        var graph = new GraphBuilder()
            .Node("Web", NodeType.Client, """{"requestsPerSecond":300}""")
            .Node("Gw", NodeType.Gateway)
            .Connect("Web", "Gw")
            .Build();

        Assert.False(_rule.Evaluate(graph).IsApplicable);
    }
}

public class EdgeAccessTests
{
    [Fact]
    public void ADeclaredWrite_DoesNotWantACache()
    {
        var writers = new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Worker", NodeType.Worker).Node("Db", NodeType.Database)
            .Connect("Api", "Db", protocol: ProtocolType.Sql, access: "write")
            .Connect("Worker", "Db", protocol: ProtocolType.Sql, access: "write")
            .Build();

        Assert.Empty(new MissingCacheRule().Evaluate(writers).Findings);
    }

    [Fact]
    public void UndeclaredEdges_StillCount()
    {
        var undeclared = new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Svc", NodeType.Service).Node("Db", NodeType.Database)
            .Connect("Api", "Db", protocol: ProtocolType.Sql)
            .Connect("Svc", "Db", protocol: ProtocolType.Sql)
            .Build();

        Assert.Single(new MissingCacheRule().Evaluate(undeclared).Findings);
    }

    [Fact]
    public void DeclaredWriters_GetTheRealSentence()
    {
        var declared = new GraphBuilder()
            .Node("Users", NodeType.Service).Node("Orders", NodeType.Service).Node("Db", NodeType.Database)
            .Connect("Users", "Db", protocol: ProtocolType.Sql, access: "write")
            .Connect("Orders", "Db", protocol: ProtocolType.Sql, access: "write")
            .Build();

        var strong = Assert.Single(new SharedDatabaseRule().Evaluate(declared).Findings);
        Assert.Equal("finding.SHARED_DATABASE.writers.message", strong.Message.Key());

        var silent = new GraphBuilder()
            .Node("Users", NodeType.Service).Node("Orders", NodeType.Service).Node("Db", NodeType.Database)
            .Connect("Users", "Db", protocol: ProtocolType.Sql)
            .Connect("Orders", "Db", protocol: ProtocolType.Sql)
            .Build();

        var conservative = Assert.Single(new SharedDatabaseRule().Evaluate(silent).Findings);
        Assert.Equal("finding.SHARED_DATABASE.shared.message", conservative.Message.Key());
    }
}

public class TopicTests
{
    [Fact]
    public void ATopicDuplicatesWhereAQueueDivides()
    {
        var queue = Brokered(NodeType.Queue);
        var topic = Brokered(NodeType.Topic);

        var viaQueue = LoadModel.Of(queue.Graph);
        var viaTopic = LoadModel.Of(topic.Graph);

        Assert.Equal(100m, viaQueue.LoadOf(queue.Builder.IdOf("C1")));
        Assert.Equal(300m, viaTopic.LoadOf(topic.Builder.IdOf("C1")));
    }

    [Fact]
    public void ATopicNobodySubscribesTo_IsTheSameSilenceAsAQueue()
    {
        var builder = new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Events", NodeType.Topic)
            .Connect("Api", "Events", synchronous: false, protocol: ProtocolType.Amqp);

        var finding = Assert.Single(new QueueNoConsumerRule().Evaluate(builder.Build()).Findings);

        Assert.Equal("finding.QUEUE_NO_CONSUMER.unconsumed.message", finding.Message.Key());
        Assert.Equal("finding.QUEUE_NO_CONSUMER.unconsumed.suggestion.topic", finding.Suggestion.Key());
    }

    [Fact]
    public void ATopicsFanOut_IsNotCoupling()
    {
        var builder = new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Events", NodeType.Topic)
            .Nodes(NodeType.Worker, "W1", "W2", "W3", "W4", "W5", "W6", "W7", "W8")
            .Connect("Api", "Events", synchronous: false, protocol: ProtocolType.Amqp);

        foreach (var w in new[] { "W1", "W2", "W3", "W4", "W5", "W6", "W7", "W8" })
            builder.Connect("Events", w, synchronous: false, protocol: ProtocolType.Amqp);

        Assert.DoesNotContain(
            new HighCouplingRule().Evaluate(builder.Build()).Findings,
            f => f.Elements.Contains(builder.NodeElement("Events")));
    }

    [Fact]
    public void AmqpWithATopicAtEitherEnd_IsTheBrokerTalking()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Events", NodeType.Topic).Node("Worker", NodeType.Worker)
            .Connect("Api", "Events", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Events", "Worker", synchronous: false, protocol: ProtocolType.Amqp)
            .Build();

        Assert.Empty(new ProtocolMismatchRule().Evaluate(graph).Findings);
    }

    [Fact]
    public void TheStagedPipelineWorksThroughTopicsToo()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Cola", NodeType.Queue).Node("Events", NodeType.Topic)
            .Node("Worker", NodeType.Worker)
            .Connect("Api", "Cola", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Cola", "Events", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Events", "Worker", synchronous: false, protocol: ProtocolType.Amqp)
            .Build();

        Assert.Empty(new QueueNoConsumerRule().Evaluate(graph).Findings);
    }

    private static (ArchitectureGraph Graph, GraphBuilder Builder) Brokered(NodeType broker)
    {
        var builder = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"requestsPerSecond":300}""")
            .Node("Broker", broker)
            .Nodes(NodeType.Worker, "C1", "C2", "C3")
            .Connect("Api", "Broker", synchronous: false, protocol: ProtocolType.Amqp);

        foreach (var c in new[] { "C1", "C2", "C3" })
            builder.Connect("Broker", c, synchronous: false, protocol: ProtocolType.Amqp);

        return (builder.Build(), builder);
    }
}
