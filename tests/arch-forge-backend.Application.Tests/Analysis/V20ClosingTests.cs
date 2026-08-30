using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis;

public class V20ClosingTests
{
    private const string ApiSettings = """{"authentication":true,"rateLimit":9000""";

    [Fact]
    public void SpreadingOverZones_DoesNotChangeWhatTheAnalyzerBills()
    {
        var stacked = new GraphBuilder()
            .Node("Users", NodeType.Client, """{"requestsPerSecond":900}""")
            .Node("Db", NodeType.Database, """{"replicas":1}""")
            .Node("Obs", NodeType.Observability)
            .Node("Api", NodeType.Api, $$"""{{ApiSettings}},"instances":3,"zone":"a"}""")
            .Connect("Users", "Api")
            .Connect("Api", "Db", timeoutSeconds: 3, fallback: true)
            .Connect("Obs", "Api", timeoutSeconds: 5, fallback: true)
            .Connect("Obs", "Db", timeoutSeconds: 5, fallback: true)
            .Build();

        var spread = new GraphBuilder()
            .Node("Users", NodeType.Client, """{"requestsPerSecond":900}""")
            .Node("Db", NodeType.Database, """{"replicas":1}""")
            .Node("Obs", NodeType.Observability)
            .Node("Api a", NodeType.Api, $$"""{{ApiSettings}},"zone":"a"}""")
            .Node("Api b", NodeType.Api, $$"""{{ApiSettings}},"zone":"b"}""")
            .Node("Api c", NodeType.Api, $$"""{{ApiSettings}},"zone":"c"}""")
            .Connect("Users", "Api a").Connect("Users", "Api b").Connect("Users", "Api c")
            .Connect("Api a", "Db", timeoutSeconds: 3, fallback: true)
            .Connect("Api b", "Db", timeoutSeconds: 3, fallback: true)
            .Connect("Api c", "Db", timeoutSeconds: 3, fallback: true)
            .Connect("Obs", "Api a", timeoutSeconds: 5, fallback: true)
            .Connect("Obs", "Api b", timeoutSeconds: 5, fallback: true)
            .Connect("Obs", "Api c", timeoutSeconds: 5, fallback: true)
            .Connect("Obs", "Db", timeoutSeconds: 5, fallback: true)
            .Build();

        var one = ReferenceArchitecture.Analyze(stacked);
        var three = ReferenceArchitecture.Analyze(spread);

        Assert.Equal(12, ApiBill(one));
        Assert.Equal(12, ApiBill(three));

        Assert.Equal(one.Cost.Total, three.Cost.Total);
        Assert.Equal(CostModel.Of(stacked), one.Cost.Total);
        Assert.Equal(CostModel.Of(spread), three.Cost.Total);
        Assert.Equal(one.ScoreOf(Area.Cost), three.ScoreOf(Area.Cost));

        Assert.Single(one.Cost.Components, c => c.PerInstance == CostModel.Api);
        Assert.Equal(3, three.Cost.Components.Count(c => c.PerInstance == CostModel.Api));

        static int ApiBill(AnalysisResult result) => result.Cost.Components
            .Where(component => component.PerInstance == CostModel.Api)
            .Sum(component => component.Total);
    }
}
