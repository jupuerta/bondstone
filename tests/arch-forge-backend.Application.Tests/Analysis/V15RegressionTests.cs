using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis;

/// <summary>
/// V15 E7. The QA bench of docs/V15/QA-analisis-V15.md as regression tests - the part of it that
/// aged best from V14: <strong>pairs</strong>. Two drawings that differ in exactly one thing must
/// differ in exactly one thing, and the whole analyzer is what says so, not one rule. Every one of
/// these was a bug at the start of the sweep, and each fix landed in a different rule; asserting
/// them here over the twenty-four together is what keeps the next rule from undoing one quietly.
///
/// And <strong>the ceiling</strong>: the best architecture the palette can draw, pinned at 100
/// with nothing but green ticks. It was 96 at the start of the sweep, because a CDN could not be
/// anything but a Critical SPOF. A regression on any rule shows up here first.
/// </summary>
public class V15RegressionTests
{
    // ---- Pair 1. Wiring the telemetry changes Observability and nothing else (A3, B9) -------

    [Fact]
    public void WiringTheTelemetry_MovesObservabilityAndNoOtherArea()
    {
        // The canvas draws an edge synchronous and unconfigured. Until V15 A3, wiring the three
        // scrape edges NO_OBSERVABILITY asks for cost 20 points of Resilience and a Critical that
        // said the Api's outage "propagates backwards" - into the metrics sink.
        static GraphBuilder Base() => new GraphBuilder()
            .Node("Api", NodeType.Api, """{"authentication":true,"rateLimit":1000}""")
            .Node("Svc", NodeType.Service)
            .Node("Db", NodeType.Database, """{"replicas":2}""")
            .Connect("Api", "Svc", timeoutSeconds: 3, fallback: true)
            .Connect("Svc", "Db", timeoutSeconds: 3, fallback: true, protocol: ProtocolType.Sql);

        var bare = ReferenceArchitecture.Analyze(Base().Build());
        var wired = ReferenceArchitecture.Analyze(Base()
            .Node("Obs", NodeType.Observability)
            .Connect("Obs", "Api").Connect("Obs", "Svc").Connect("Obs", "Db")
            .Build());

        foreach (var area in Enum.GetValues<Area>().Where(a => a != Area.Observability))
            Assert.Equal(bare.ScoreOf(area), wired.ScoreOf(area));

        Assert.True(wired.ScoreOf(Area.Observability) > bare.ScoreOf(Area.Observability));
        Assert.Equal(100, wired.ScoreOf(Area.Observability));
    }

    // ---- Pair 2. Declaring redundancy and drawing it are the same fact (B1, B4, B11) --------

    [Fact]
    public void DeclaredAndDrawnRedundancy_ScoreTheSame()
    {
        // `instances: 2` behind a balancer and two boxes behind it are one fact said two ways.
        // Until V15 B1 the first left the balancer a Critical SPOF - fifteen points of
        // Availability for using the field V14 B4 added.
        var declared = ReferenceArchitecture.Analyze(new GraphBuilder()
            .Node("Web", NodeType.Client).Node("LB", NodeType.LoadBalancer)
            .Node("Api", NodeType.Api, """{"authentication":true,"rateLimit":1000,"instances":2}""")
            .Node("Db", NodeType.Database, """{"replicas":2}""").Node("Obs", NodeType.Observability)
            .Connect("Web", "LB").Connect("LB", "Api", timeoutSeconds: 3, fallback: true)
            .Connect("Api", "Db", timeoutSeconds: 3, fallback: true, protocol: ProtocolType.Sql)
            .Connect("Obs", "LB").Connect("Obs", "Api").Connect("Obs", "Db")
            .Build());

        var drawn = ReferenceArchitecture.Analyze(new GraphBuilder()
            .Node("Web", NodeType.Client).Node("LB", NodeType.LoadBalancer)
            .Node("Api1", NodeType.Api, """{"authentication":true,"rateLimit":1000}""")
            .Node("Api2", NodeType.Api, """{"authentication":true,"rateLimit":1000}""")
            .Node("Db", NodeType.Database, """{"replicas":2}""").Node("Obs", NodeType.Observability)
            .Connect("Web", "LB").Connect("LB", "Api1", timeoutSeconds: 3, fallback: true).Connect("LB", "Api2", timeoutSeconds: 3, fallback: true)
            .Connect("Api1", "Db", timeoutSeconds: 3, fallback: true, protocol: ProtocolType.Sql)
            .Connect("Api2", "Db", timeoutSeconds: 3, fallback: true, protocol: ProtocolType.Sql)
            .Connect("Obs", "LB").Connect("Obs", "Api1").Connect("Obs", "Api2").Connect("Obs", "Db")
            .Build());

        Assert.Equal(100, declared.ScoreOf(Area.Availability));
        Assert.Equal(100, drawn.ScoreOf(Area.Availability));

        // And two replicas of one service are not two services on a database (B2): neither
        // drawing pays Coupling or Scalability for the second copy.
        Assert.Equal(100, drawn.ScoreOf(Area.Coupling));
        Assert.Equal(100, drawn.ScoreOf(Area.Scalability));
    }

    // ---- Pair 3. Adding the CDN MISSING_CDN asks for raises the score (C2) ----------------------

    [Fact]
    public void AddingTheCdnTheAnalyzerAsksFor_RaisesTheScore()
    {
        // Until V15 C2 it lowered it: Scalability gained 20 and Availability lost 15, because the
        // edge cache was a Critical SPOF nothing but a second CDN could clear.
        static GraphBuilder Behind(bool cdn)
        {
            var b = new GraphBuilder()
                .Node("Web", NodeType.Client, """{"requestsPerSecond":3000}""")
                .Node("Api", NodeType.Api, """{"authentication":true,"rateLimit":5000,"instances":10}""")
                .Node("Db", NodeType.Database, """{"replicas":2,"readReplicas":2}""")
                .Node("Obs", NodeType.Observability)
                .Connect("Api", "Db", timeoutSeconds: 3, fallback: true, protocol: ProtocolType.Sql)
                .Connect("Obs", "Api", timeoutSeconds: 5, fallback: true).Connect("Obs", "Db", timeoutSeconds: 5, fallback: true);

            if (cdn)
                b.Node("Edge", NodeType.Cdn, """{"hitRatio":0.9}""").Connect("Web", "Edge").Connect("Edge", "Api", timeoutSeconds: 3, fallback: true);
            else
                b.Connect("Web", "Api");

            return b;
        }

        var without = ReferenceArchitecture.Analyze(Behind(cdn: false).Build());
        var with = ReferenceArchitecture.Analyze(Behind(cdn: true).Build());

        Assert.Contains(without.Findings, f => f.Code == RuleCodes.MissingCdn && f.Severity == Severity.Warning);
        Assert.True(with.Score > without.Score, $"{without.Score} -> {with.Score}");
        Assert.Equal(100, with.ScoreOf(Area.Availability));
    }

    // ---- The ceiling ---------------------------------------------------------------------------

    [Fact]
    public void TheBestArchitectureThePaletteCanDraw_ScoresOneHundred()
    {
        // Everything the product teaches, drawn at once: an edge cache, a balancer over two
        // gateways with authentication and a limit, the identity provider covered, an Api and a
        // Service with instances declared, a cache, a replicated database, a queue with a
        // dead-letter queue and a worker, every call timed and covered, every component wired to
        // the telemetry asynchronously. 96 at the start of the V15 sweep; the Critical it carried
        // was the CDN. If this ever drops, a rule has started charging for something correct.
        var b = new GraphBuilder()
            .Node("Web", NodeType.Client, """{"requestsPerSecond":500}""")
            .Node("Edge", NodeType.Cdn, """{"hitRatio":0.9}""")
            .Node("LB", NodeType.LoadBalancer, """{"healthCheck":true}""")
            .Node("Gw1", NodeType.Gateway, """{"authentication":true,"rateLimit":5000}""")
            .Node("Gw2", NodeType.Gateway, """{"authentication":true,"rateLimit":5000}""")
            .Node("IdP", NodeType.IdentityProvider)
            .Node("Api", NodeType.Api, """{"authentication":true,"rateLimit":5000,"instances":3}""")
            .Node("Svc", NodeType.Service, """{"instances":3}""")
            .Node("Cache", NodeType.Cache, """{"hitRatio":0.8}""")
            .Node("Db", NodeType.Database, """{"replicas":2,"readReplicas":2}""")
            .Node("Cola", NodeType.Queue, """{"deadLetterQueue":true,"maxRetries":3}""")
            .Node("Worker", NodeType.Worker, """{"instances":2}""")
            .Node("Obs", NodeType.Observability)
            .Connect("Web", "Edge")
            .Connect("Edge", "LB", timeoutSeconds: 3, fallback: true)
            .Connect("LB", "Gw1", timeoutSeconds: 3, fallback: true).Connect("LB", "Gw2", timeoutSeconds: 3, fallback: true)
            .Connect("Gw1", "Api", timeoutSeconds: 3, fallback: true).Connect("Gw2", "Api", timeoutSeconds: 3, fallback: true)
            .Connect("Gw1", "IdP", timeoutSeconds: 2, fallback: true).Connect("Gw2", "IdP", timeoutSeconds: 2, fallback: true)
            .Connect("Api", "Cache", timeoutSeconds: 1, fallback: true)
            .Connect("Api", "Svc", timeoutSeconds: 3, fallback: true)
            .Connect("Svc", "Db", timeoutSeconds: 3, fallback: true, protocol: ProtocolType.Sql)
            .Connect("Api", "Cola", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Cola", "Worker", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Worker", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql);

        foreach (var name in new[] { "Edge", "LB", "Gw1", "Gw2", "Api", "Svc", "Cache", "Db", "Cola", "Worker" })
            b.Connect("Obs", name, synchronous: false);

        var result = ReferenceArchitecture.Analyze(b.Build());

        Assert.Equal(100, result.Score);
        Assert.All(result.Areas, area => Assert.Equal(100, area.Score));
        Assert.All(result.Findings, f => Assert.Equal(Severity.Good, f.Severity));
    }

    // ---- The floor of the sketch (D1 is the client's; this is what the client reads) ----------

    [Fact]
    public void TheSketchesOfTheV14Table_ScoreOverFiveAreasAndTwoComponents()
    {
        // `Db -> Client` came back at 97 over five areas and labelled "solid" after V14. The
        // number is still the number; what V15 D1 changed is that the client withholds the word
        // below three connected components. This pins the half the client relies on: the drawing
        // has two components and the number applies to five or more areas, which is exactly the
        // case D1's threshold of areas alone could no longer catch.
        var result = ReferenceArchitecture.Analyze(new GraphBuilder()
            .Node("Db", NodeType.Database).Node("Web", NodeType.Client).Connect("Db", "Web").Build());

        Assert.True(result.Score >= 95);
        Assert.True(result.Areas.Count(a => a.State == AreaState.Scored) >= 5);
    }
}
