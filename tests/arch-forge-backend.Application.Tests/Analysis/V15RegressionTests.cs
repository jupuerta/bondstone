using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis;

public class V15RegressionTests
{

    [Fact]
    public void WiringTheTelemetry_MovesObservabilityAndNoOtherArea()
    {
        static GraphBuilder Base() => new GraphBuilder()
            .Node("Api", NodeType.Api, """{"authentication":true,"rateLimit":1000}""")
            .Node("Svc", NodeType.Service)
            .Node("Db", NodeType.Database, """{"replicas":2}""")
            .Connect("Api", "Svc", timeoutSeconds: 3, fallback: true)
            .Connect("Svc", "Db", timeoutSeconds: 3, fallback: true);

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


    [Fact]
    public void DeclaredAndDrawnRedundancy_ScoreTheSame()
    {
        var declared = ReferenceArchitecture.Analyze(new GraphBuilder()
            .Node("Web", NodeType.Client).Node("LB", NodeType.LoadBalancer)
            .Node("Api", NodeType.Api, """{"authentication":true,"rateLimit":1000,"instances":2}""")
            .Node("Db", NodeType.Database, """{"replicas":2}""").Node("Obs", NodeType.Observability)
            .Connect("Web", "LB").Connect("LB", "Api", timeoutSeconds: 3, fallback: true)
            .Connect("Api", "Db", timeoutSeconds: 3, fallback: true)
            .Connect("Obs", "LB").Connect("Obs", "Api").Connect("Obs", "Db")
            .Build());

        var drawn = ReferenceArchitecture.Analyze(new GraphBuilder()
            .Node("Web", NodeType.Client).Node("LB", NodeType.LoadBalancer)
            .Node("Api1", NodeType.Api, """{"authentication":true,"rateLimit":1000}""")
            .Node("Api2", NodeType.Api, """{"authentication":true,"rateLimit":1000}""")
            .Node("Db", NodeType.Database, """{"replicas":2}""").Node("Obs", NodeType.Observability)
            .Connect("Web", "LB").Connect("LB", "Api1", timeoutSeconds: 3, fallback: true).Connect("LB", "Api2", timeoutSeconds: 3, fallback: true)
            .Connect("Api1", "Db", timeoutSeconds: 3, fallback: true)
            .Connect("Api2", "Db", timeoutSeconds: 3, fallback: true)
            .Connect("Obs", "LB").Connect("Obs", "Api1").Connect("Obs", "Api2").Connect("Obs", "Db")
            .Build());

        Assert.Equal(100, declared.ScoreOf(Area.Availability));
        Assert.Equal(100, drawn.ScoreOf(Area.Availability));

        Assert.Equal(100, drawn.ScoreOf(Area.Coupling));
        Assert.Equal(100, drawn.ScoreOf(Area.Scalability));
    }


    [Fact]
    public void AddingTheCdnTheAnalyzerAsksFor_RaisesTheScore()
    {
        static GraphBuilder Behind(bool cdn)
        {
            var b = new GraphBuilder()
                .Node("Web", NodeType.Client, """{"requestsPerSecond":3000}""")
                .Node("Api", NodeType.Api, """{"authentication":true,"rateLimit":5000,"instances":10}""")
                .Node("Db", NodeType.Database, """{"replicas":2,"readReplicas":2}""")
                .Node("Obs", NodeType.Observability)
                .Connect("Api", "Db", timeoutSeconds: 3, fallback: true)
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


    // V20 C2: the drawing, not the claim. A perfect score is still reachable on all seven areas —
    // but the copies have to be earned. With 500 rps and a CDN swallowing nine tenths of them, three
    // Apis, three Services and five database copies served 25 rps, which is what the cost area is
    // there to notice. Same shape, the traffic that pays for it, and one replica fewer.
    [Fact]
    public void TheBestArchitectureThePaletteCanDraw_ScoresOneHundred()
    {
        var b = new GraphBuilder()
            .Node("Web", NodeType.Client, """{"requestsPerSecond":5000}""")
            .Node("Edge", NodeType.Cdn, """{"hitRatio":0.9}""")
            .Node("LB", NodeType.LoadBalancer, """{"healthCheck":true}""")
            .Node("Gw1", NodeType.Gateway, """{"authentication":true,"rateLimit":5000}""")
            .Node("Gw2", NodeType.Gateway, """{"authentication":true,"rateLimit":5000}""")
            .Node("IdP", NodeType.IdentityProvider)
            .Node("Api", NodeType.Api, """{"authentication":true,"rateLimit":5000,"instances":2}""")
            .Node("Svc", NodeType.Service, """{"instances":2}""")
            .Node("Cache", NodeType.Cache, """{"hitRatio":0.8}""")
            .Node("Db", NodeType.Database, """{"replicas":1,"readReplicas":2}""")
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
            .Connect("Svc", "Db", timeoutSeconds: 3, fallback: true)
            .Connect("Api", "Cola", synchronous: false)
            .Connect("Cola", "Worker", synchronous: false)
            .Connect("Worker", "Db", timeoutSeconds: 5, fallback: true);

        foreach (var name in new[] { "Edge", "LB", "Gw1", "Gw2", "Api", "Svc", "Cache", "Db", "Cola", "Worker" })
            b.Connect("Obs", name, synchronous: false);

        var result = ReferenceArchitecture.Analyze(b.Build());

        Assert.Equal(100, result.Score);
        Assert.All(result.Areas, area => Assert.Equal(100, area.Score));
        Assert.All(result.Findings, f => Assert.Equal(Severity.Good, f.Severity));
    }


    [Fact]
    public void TheSketchesOfTheV14Table_ScoreOverFiveAreasAndTwoComponents()
    {
        var result = ReferenceArchitecture.Analyze(new GraphBuilder()
            .Node("Db", NodeType.Database).Node("Web", NodeType.Client).Connect("Db", "Web").Build());

        Assert.True(result.Score >= 95);
        Assert.True(result.Areas.Count(a => a.State == AreaState.Scored) >= 5);
    }
}
