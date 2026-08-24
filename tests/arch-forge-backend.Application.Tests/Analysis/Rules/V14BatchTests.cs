using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

/// <summary>
/// V14 B13. "You are not watching this box" and "you cannot see anything" are different claims,
/// and with one Warning-only rule in the area the second one could never cost more than fifteen
/// points: total blindness read "aceptable".
/// </summary>
public class ObservabilityFloorTests
{
    private readonly NoObservabilityRule _rule = new();

    [Fact]
    public void TotalBlindness_OnARealDiagram_IsCritical()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Svc", NodeType.Service).Node("Db", NodeType.Database)
            .Connect("Api", "Svc").Connect("Svc", "Db", protocol: ProtocolType.Sql)
            .Build();

        var findings = _rule.Evaluate(graph).Findings;

        Assert.Equal(3, findings.Count);
        Assert.All(findings, f => Assert.Equal(Severity.Critical, f.Severity));
    }

    [Fact]
    public void OneTelemetryBox_TurnsTheRestBackIntoHomework()
    {
        // The moment anything is watching, the claim changes back to "this box is not": the same
        // wiring gap, the Warning it always was.
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
    public void TwoBoxes_AreASketchAndKeepTheWarning()
    {
        // Below three observables the diagram is a sketch (the SYNC_CHAIN threshold), and a
        // sketch with no telemetry is homework, not an incident - minimal-api-db must not open red.
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api).Node("Db", NodeType.Database)
            .Connect("Api", "Db", protocol: ProtocolType.Sql)
            .Build();

        Assert.All(_rule.Evaluate(graph).Findings, f => Assert.Equal(Severity.Warning, f.Severity));
    }

    [Fact]
    public void TheIdentityProvider_IsNotYoursToWatch()
    {
        // V14 B12 (observación). Auth0 is somebody else's service: you neither wire it to your
        // telemetry nor give it instances - the same third group as ExternalService and the Cdn.
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

/// <summary>
/// V14 §4.3. C4 in the sweep: a Client declaring a million rps in front of a rate limit of 100,
/// and everything behind the door came out perfect - the limit had converted "your system cannot
/// serve your users" into "your system is healthy".
/// </summary>
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
        // The two figures travel raw so the card can be redone by hand.
        Assert.Equal("1000000", finding.Explanation.Arg("demandRps"));
        Assert.Equal("100", finding.Explanation.Arg("admittedRps"));
    }

    [Fact]
    public void StandingInForCapacity_IsAWarning()
    {
        // 1000 declared, 300 admitted: 70 % turned away - the limit is no longer shedding a peak.
        Assert.Equal(Severity.Warning, Assert.Single(_rule.Evaluate(Door(1_000, 300)).Findings).Severity);
    }

    [Fact]
    public void SheddingAPeak_IsTheLimitDoingItsJob()
    {
        // 40 % over capacity rejected at the door is what NO_RATE_LIMIT asks the door to be able
        // to do; flagging it would be the two rules contradicting each other.
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

/// <summary>V14 §4.3, the debt of §3.3: the rule that justifies the Cdn type (V2 D3.2).</summary>
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
        // With its default hit ratio the CDN absorbs enough that the door drops below the
        // threshold - the load model answers before the rule has to look.
        var absorbing = new GraphBuilder()
            .Node("Web", NodeType.Client, """{"requestsPerSecond":2000}""")
            .Node("Edge", NodeType.Cdn)
            .Node("Gw", NodeType.Gateway)
            .Connect("Web", "Edge").Connect("Edge", "Gw")
            .Build();

        Assert.False(_rule.Evaluate(absorbing).IsApplicable);

        // And when everything misses, the door is still busy - and still clean, because the rule
        // asks whether the edge exists, not whether it is performing today.
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
        // Below the threshold one door handles it and a CDN is an optimisation. Not applicable
        // rather than passed: nothing was checked.
        var graph = new GraphBuilder()
            .Node("Web", NodeType.Client, """{"requestsPerSecond":300}""")
            .Node("Gw", NodeType.Gateway)
            .Connect("Web", "Gw")
            .Build();

        Assert.False(_rule.Evaluate(graph).IsApplicable);
    }
}

/// <summary>V14 §4.2. What the call does with the data changes what the rules may say about it.</summary>
public class EdgeAccessTests
{
    [Fact]
    public void ADeclaredWrite_DoesNotWantACache()
    {
        // T11: an Api and a Worker both writing. "Put a cache in front" was the advice, and a
        // cache answers reads - there is nothing of these calls it could absorb.
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
        // Absent means both (DT.6): every edge drawn before v7 behaves exactly as it did.
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
        // The sentence SHARED_DATABASE always wanted to say - two services *writing* one schema -
        // said only when the drawing declares it, never guessed from the default.
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

/// <summary>
/// V14 §3.1. Publish-subscribe beside point-to-point: every subscriber gets a copy where a
/// queue's consumers compete - the difference between spreading work and broadcasting a fact.
/// </summary>
public class TopicTests
{
    [Fact]
    public void ATopicDuplicatesWhereAQueueDivides()
    {
        // The cost lesson of event fan-out, and the mechanism the model already owned: 300 rps
        // published to a queue is 100 per competing consumer; to a topic it is 300 per subscriber.
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
        // Broadcasting is half of why you use one: eight subscribers are eight copies of a fact,
        // not eight dependencies the topic holds.
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
        // Queue -> Topic -> Worker: routing between brokers ends in somebody who does the work,
        // exactly as B8 established for queue chains.
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
