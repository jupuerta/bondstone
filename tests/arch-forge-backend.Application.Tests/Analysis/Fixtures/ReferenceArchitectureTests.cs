using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Fixtures;

public class ReferenceArchitectureTests
{
    [Fact]
    public void MinimalApiDb_IsStillPerfectOnTheFourAreasV2Scored()
    {
        var result = ReferenceArchitecture.Analyze("minimal-api-db");

        Assert.Equal(100, result.ScoreOf(Area.Availability));
        Assert.Equal(100, result.ScoreOf(Area.Resilience));
        Assert.Equal(100, result.ScoreOf(Area.Coupling));
        Assert.Equal(100, result.ScoreOf(Area.Scalability));

        // 85 desde el 30-08 (QA §7.3.6): la puerta que no declara autenticación con datos detrás es
        // crítico, el mismo peso que declararla abierta. Antes 95, y la nota global 98.
        Assert.Equal(85, result.ScoreOf(Area.Security));
        Assert.Equal(93, result.ScoreOf(Area.Observability));
        Assert.Equal(96, result.Score);

        Assert.Equal(new AnalysisSummary(Critical: 1, Warning: 2, Info: 0, Passed: 12), result.Summary);
    }

    [Fact]
    public void MinimalApiDb_ScoresTheSixAreasAndStandsDownOnTheSeventh()
    {
        var result = ReferenceArchitecture.Analyze("minimal-api-db");

        Assert.All(
            result.Areas.Where(area => area.Area != Area.Cost),
            area => Assert.Equal(AreaState.Scored, area.State));

        Assert.Equal(AreaState.NotApplicable, result.StateOf(Area.Cost));
        Assert.Null(result.ScoreOf(Area.Cost));
    }

    [Fact]
    public void MinimalApiDb_RetryAmplificationHasNothingToCheck()
    {
        var result = ReferenceArchitecture.Analyze("minimal-api-db");

        Assert.DoesNotContain(RuleCodes.RetryAmplification, result.Findings.Select(f => f.Code));
        Assert.Equal(AreaState.Scored, result.StateOf(Area.Resilience));
    }

    [Fact]
    public void GatewayMicroservices_ScoresEightyFour()
    {
        var result = ReferenceArchitecture.Analyze("gateway-microservices");

        Assert.Equal(75, result.ScoreOf(Area.Availability));

        Assert.Equal(65, result.ScoreOf(Area.Resilience));

        Assert.Equal(95, result.ScoreOf(Area.Coupling));
        // Ver `WorstCase_ScoresSeventyThree`: `MISSING_CACHE` en `Info` no resta.
        Assert.Equal(100, result.ScoreOf(Area.Scalability));
        // 30-08 (QA §7.3): la puerta sin declarar pasa de 95 a 85, y la ceguera se cobra una sola vez
        // — el crítico sin los avisos por caja — y sube de 73 a 85. La nota global no se mueve.
        Assert.Equal(85, result.ScoreOf(Area.Security));

        Assert.Equal(85, result.ScoreOf(Area.Observability));

        Assert.Equal(84, result.Score);

        Assert.Equal(new AnalysisSummary(Critical: 7, Warning: 3, Info: 1, Passed: 8), result.Summary);
    }

    [Fact]
    public void GatewayMicroservices_FlagsTheGatewayThroughPatternB()
    {
        var result = ReferenceArchitecture.Analyze("gateway-microservices");

        Assert.Contains(result.Findings, f =>
            f.Code == RuleCodes.Spof &&
            f.Severity == Severity.Critical &&
            f.Message.Describe() == "finding.SPOF.unreachable.message(count=4, node=Gateway)");
    }

    [Fact]
    public void GatewayMicroservices_GradesSpofByBlastRadius()
    {
        var result = ReferenceArchitecture.Analyze("gateway-microservices");
        var spofs = result.Findings.Where(f => f.Code == RuleCodes.Spof).ToList();

        Assert.Contains(spofs, f => f.Severity == Severity.Critical && f.Message.Arg("node") == "Db");
        Assert.Contains(spofs, f => f.Severity == Severity.Warning && f.Message.Arg("node") == "Payments");
    }

    [Fact]
    public void ResilientAsync_ScoresNinetyTwo()
    {
        var result = ReferenceArchitecture.Analyze("resilient-async");

        Assert.Equal(95, result.ScoreOf(Area.Availability));

        Assert.Equal(95, result.ScoreOf(Area.Resilience));

        Assert.Equal(100, result.ScoreOf(Area.Coupling));
        Assert.Equal(100, result.ScoreOf(Area.Scalability));

        // 30-08 (QA §7.3): dos puertas sin declarar, las dos críticas ahora (93 → 78); la ceguera
        // cobrada una vez (68 → 85). La nota global no se mueve.
        Assert.Equal(78, result.ScoreOf(Area.Security));

        Assert.Equal(85, result.ScoreOf(Area.Observability));

        Assert.Equal(92, result.Score);

        Assert.Equal(new AnalysisSummary(Critical: 3, Warning: 2, Info: 0, Passed: 14), result.Summary);
    }

    [Fact]
    public void ResilientAsync_KeepsTheDatabaseAndDropsTheQueueAndTheCache()
    {
        var result = ReferenceArchitecture.Analyze("resilient-async");
        var spofs = result.Findings.Where(f => f.Code == RuleCodes.Spof).ToList();

        Assert.Equal(["Db"], spofs.Select(f => f.Message.Arg("node")));
        Assert.Equal(Severity.Warning, Assert.Single(spofs).Severity);
    }

    [Fact]
    public void ResilientAsync_TellsTheDatabaseWhatToDoAboutIt()
    {
        var result = ReferenceArchitecture.Analyze("resilient-async");

        Assert.Equal(
            "finding.SPOF.suggestion.database",
            Assert.Single(result.Findings, f => f.Code == RuleCodes.Spof).Suggestion.Key());
    }

    [Fact]
    public void ResilientAsync_NeverFlagsTheLoadBalancer()
    {
        var result = ReferenceArchitecture.Analyze("resilient-async");

        Assert.DoesNotContain(result.Findings, f => f.Code == RuleCodes.Spof && f.Message.Arg("node") == "LB");
    }

    [Fact]
    public void ResilientAsync_DoesNotCountTheCacheAsAConsumerOfItself()
    {
        var result = ReferenceArchitecture.Analyze("resilient-async");

        Assert.Contains(result.Findings, f => f.Code == RuleCodes.MissingCache && f.Severity == Severity.Good);
    }

    [Fact]
    public void ResilientAsync_CircuitBreakerAndFallbackPreventTheEscalation()
    {
        var result = ReferenceArchitecture.Analyze("resilient-async");

        Assert.Contains(result.Findings, f => f.Code == RuleCodes.MissingResilience && f.Severity == Severity.Good);
    }

    [Fact]
    public void WorstCase_ScoresSeventyThree()
    {
        var result = ReferenceArchitecture.Analyze("worst-case");

        Assert.Equal(68, result.ScoreOf(Area.Availability));

        Assert.Equal(35, result.ScoreOf(Area.Resilience));

        Assert.Equal(65, result.ScoreOf(Area.Coupling));
        // 100 y no 95 desde V20: `MISSING_CACHE` es la única regla de Escalabilidad que salta aquí,
        // y en `Info` ya no resta. La arquitectura no ha mejorado; hemos dejado de cobrarle por una
        // sugerencia de velocidad.
        Assert.Equal(100, result.ScoreOf(Area.Scalability));
        // 30-08 (QA §7.3): 95 → 85 por la puerta sin declarar; 60 → 85 porque la ceguera de diez
        // cajas se cobra una vez y no once. 71 → 73: el peor caso sale mejor parado de la limpieza
        // que del castigo, y está medido y escrito en QA §9.6.
        Assert.Equal(85, result.ScoreOf(Area.Security));
        Assert.Equal(85, result.ScoreOf(Area.Observability));

        Assert.Equal(73, result.Score);

        Assert.Equal(13, result.Summary.Critical);
    }

    [Fact]
    public void WorstCase_FiresEverySevenV2RulesPlusTheTwoV4FindsHere()
    {
        var result = ReferenceArchitecture.Analyze("worst-case");

        Assert.Equal(
            [
                RuleCodes.Cycle, RuleCodes.HighCoupling, RuleCodes.MissingCache,
                RuleCodes.MissingResilience, RuleCodes.NoObservability, RuleCodes.RetryAmplification,
                RuleCodes.SharedDatabase, RuleCodes.Spof, RuleCodes.SyncChain, RuleCodes.UnauthenticatedEntry
            ],
            result.Findings.Where(f => f.Severity != Severity.Good).Select(f => f.Code).Distinct().Order());

        Assert.Equal(5, result.Summary.Passed);
    }

    [Theory]
    [InlineData("minimal-api-db")]
    [InlineData("worst-case")]
    [InlineData("resilient-async")]
    [InlineData("gateway-microservices")]
    public void ShufflingTheInput_DoesNotChangeTheAnalysis(string fixtureName)
    {
        var (nodes, connections) = ReferenceArchitecture.Load(fixtureName);

        var straight = ReferenceArchitecture.Analyze(new ArchitectureGraph(nodes, connections));
        var reversed = ReferenceArchitecture.Analyze(new ArchitectureGraph(
            [.. Enumerable.Reverse(nodes)],
            [.. Enumerable.Reverse(connections)]));

        Assert.Equal(Render(straight), Render(reversed));
    }

    [Fact]
    public void WorstCase_BreaksTiesByTheOrderOfTheGraph()
    {
        var result = ReferenceArchitecture.Analyze("worst-case");

        var graph = ReferenceArchitecture.Graph("worst-case");

        Assert.Equal(
            ["Db", "Orders", "Gateway", "Notifications"],
            result.Findings
                .Where(f => f.Code == RuleCodes.Spof)
                .Select(f => graph.NodesById[f.Elements[0].Id].Name));
    }

    [Fact]
    public void WorstCase_OrdersFindingsBySeverityThenAreaThenCode()
    {
        var result = ReferenceArchitecture.Analyze("worst-case");

        Assert.Equal(
            [
                RuleCodes.Spof, RuleCodes.Spof, RuleCodes.Spof,
                RuleCodes.MissingResilience, RuleCodes.MissingResilience, RuleCodes.MissingResilience,
                RuleCodes.MissingResilience, RuleCodes.MissingResilience,
                RuleCodes.RetryAmplification,
                RuleCodes.Cycle, RuleCodes.HighCoupling,
                // 30-08: la puerta sin declarar sube a los críticos, y los diez avisos de
                // observabilidad desaparecen — la ceguera es un hallazgo, no once.
                RuleCodes.UnauthenticatedEntry,
                RuleCodes.NoObservability,
                RuleCodes.Spof, RuleCodes.MissingResilience, RuleCodes.SyncChain,
                RuleCodes.SharedDatabase,
                // V20. `Info` va por debajo de todo lo que penaliza y por encima de lo que pasó, que
                // es el sitio que le corresponde: tiene algo que decir y no cuesta puntos.
                RuleCodes.MissingCache
            ],
            result.Findings.Where(f => f.Severity != Severity.Good).Select(f => f.Code));
    }

    [Fact]
    public void WorstCase_CapsMissingResilienceAtThreeTimesItsWeight()
    {
        var result = ReferenceArchitecture.Analyze("worst-case");

        Assert.Equal(6, result.Findings.Count(f => f.Code == RuleCodes.MissingResilience));
        Assert.Equal(35, result.ScoreOf(Area.Resilience));
    }

    [Theory]
    [InlineData("minimal-api-db")]
    [InlineData("gateway-microservices")]
    [InlineData("resilient-async")]
    [InlineData("worst-case")]
    public void EveryFixture_CarriesTheSixAreasAndTheScoringVersion(string fixture)
    {
        var result = ReferenceArchitecture.Analyze(fixture);

        Assert.Equal("v12", result.ScoringVersion);
        Assert.Equal(Enum.GetValues<Area>(), result.Areas.Select(a => a.Area));
        Assert.All(result.Findings, finding =>
        {
            Assert.StartsWith("finding.", finding.Message.Key());
            Assert.StartsWith("finding.", finding.Explanation.Key());
            Assert.Equal(finding.Severity == Severity.Good, finding.Suggestion is null);
            Assert.NotNull(finding.Elements);
        });
    }


    [Theory]
    [InlineData("minimal-api-db", false, true)]
    [InlineData("minimal-api-db", true, false)]
    [InlineData("resilient-async", false, true)]
    [InlineData("resilient-async", true, false)]
    [InlineData("gateway-microservices", false, true)]
    [InlineData("gateway-microservices", true, false)]
    [InlineData("worst-case", false, true)]
    [InlineData("worst-case", true, false)]
    public void WiringTheTelemetry_MovesObservabilityAndNothingElse(string fixture, bool push, bool synchronous)
    {
        var bare = ReferenceArchitecture.Analyze(fixture);
        var observed = ReferenceArchitecture.Analyze(Instrumented(fixture, push, synchronous));

        foreach (var area in Enum.GetValues<Area>().Where(a => a != Area.Observability))
            Assert.Equal(bare.ScoreOf(area), observed.ScoreOf(area));

        Assert.Equal(
            bare.Findings.Where(f => f.Area != Area.Observability).Select(Line),
            observed.Findings.Where(f => f.Area != Area.Observability).Select(Line));

        Assert.Equal(100, observed.ScoreOf(Area.Observability));
    }

    [Theory]
    [InlineData("minimal-api-db")]
    [InlineData("resilient-async")]
    [InlineData("gateway-microservices")]
    [InlineData("worst-case")]
    public void ASynchronousPushToTheTelemetry_IsStillChargedInResilience_AsAWarningAndOnlyThere(string fixture)
    {
        var bare = ReferenceArchitecture.Analyze(fixture);
        var graph = Instrumented(fixture, push: true, synchronous: true);
        var observed = ReferenceArchitecture.Analyze(graph);

        foreach (var area in Enum.GetValues<Area>().Where(a => a is not (Area.Resilience or Area.Observability)))
            Assert.Equal(bare.ScoreOf(area), observed.ScoreOf(area));

        Assert.True(observed.ScoreOf(Area.Resilience) <= bare.ScoreOf(Area.Resilience));

        var sends = graph.Connections.Where(c => NodeRoles.IsTelemetryEdge(graph, c)).Select(c => c.Id).ToHashSet();
        var onSends = observed.Findings.Where(f => f.Elements.Any(e => sends.Contains(e.Id))).ToList();

        Assert.Equal(sends.Count, onSends.Count);
        Assert.All(onSends, f =>
        {
            Assert.Equal(RuleCodes.MissingResilience, f.Code);
            Assert.Equal(Severity.Warning, f.Severity);
            Assert.Equal("finding.MISSING_RESILIENCE.telemetryPush.explanation", f.Explanation.Key());
            Assert.Equal(FixKinds.MakeEdgeAsynchronous, f.Fix?.Kind);
        });

        Assert.Equal(
            bare.Findings.Where(f => f.Area != Area.Observability && f.Severity != Severity.Good).Select(Line),
            observed.Findings.Where(f => f.Area != Area.Observability && f.Severity != Severity.Good && !onSends.Contains(f)).Select(Line));
    }

    private static ArchitectureGraph Instrumented(string fixture, bool push, bool synchronous)
    {
        var (nodes, connections) = ReferenceArchitecture.Load(fixture);

        var telemetry = new Node
        {
            Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            Type = NodeType.Observability,
            Name = "Obs"
        };
        nodes.Add(telemetry);

        var wired = 0;
        foreach (var node in nodes.Where(n => NodeRoles.IsObservable(n.Type)).ToList())
        {
            connections.Add(new Connection
            {
                Id = Guid.Parse($"ffffffff-ffff-ffff-ffff-{++wired:000000000000}"),
                SourceNodeId = push ? node.Id : telemetry.Id,
                TargetNodeId = push ? telemetry.Id : node.Id,
                Synchronous = synchronous
            });
        }

        return new ArchitectureGraph(nodes, connections);
    }

    private static string Line(RuleFinding f) =>
        $"{f.Severity} {f.Area} {f.Code} | {f.Message.Describe()} | " +
        string.Join(",", f.Elements.Select(e => $"{e.Type}:{e.Id}"));

    private static string Render(AnalysisResult result)
    {
        var lines = new List<string>
        {
            $"score={result.Score} version={result.ScoringVersion}",
            $"summary={result.Summary.Critical}/{result.Summary.Warning}/{result.Summary.Passed}"
        };

        lines.AddRange(result.Areas.Select(a => $"area {a.Area} {a.State} {a.Score}"));
        lines.AddRange(result.Findings.Select(f =>
            $"{f.Severity} {f.Area} {f.Code} | {f.Message.Describe()} | " +
            string.Join(",", f.Elements.Select(e => $"{e.Type}:{e.Id}"))));

        return string.Join("\n", lines);
    }
}
