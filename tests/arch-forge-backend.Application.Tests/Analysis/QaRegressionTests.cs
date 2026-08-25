using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis;

public class QaRegressionTests
{
    private readonly GraphBuilder _builder = new();


    [Fact]
    public void B1_AsyncPipeline_GradesThePassageByWhatItsLossActuallyCosts()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Nodes(NodeType.Queue, "Cola1", "Cola2", "Cola3")
            .Node("Worker", NodeType.Worker)
            .Connect("Api", "Cola1", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Cola1", "Cola2", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Cola2", "Cola3", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Cola3", "Worker", synchronous: false, protocol: ProtocolType.Amqp);

        var findings = new SpofRule().Evaluate(_builder.Build()).Findings;

        var cola1 = Assert.Single(findings, f => f.Elements.Contains(_builder.NodeElement("Cola1")));
        Assert.Equal(Severity.Warning, cola1.Severity);
        Assert.Equal("finding.SPOF.unreachable.message", cola1.Message.Key());
        Assert.Equal("finding.SPOF.explanation.covered", cola1.Explanation.Key());

        var api = Assert.Single(findings, f => f.Elements.Contains(_builder.NodeElement("Api")));
        Assert.Equal(Severity.Critical, api.Severity);
        Assert.Equal("finding.SPOF.explanation.measured", api.Explanation.Key());
    }

    [Fact]
    public void B1_ConnectingObservability_DoesNotChangeTheSpofOutcome()
    {
        var bare = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql);

        Assert.Empty(new SpofRule().Evaluate(bare.Build()).Findings);

        var observed = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql)
            .Connect("Api", "Obs", synchronous: false);

        Assert.Empty(new SpofRule().Evaluate(observed.Build()).Findings);
    }

    [Fact]
    public void B1_TheWellConfiguredMinimum_WithTelemetry_ReachesAHundred()
    {
        _builder
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Node("Db", NodeType.Database)
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql)
            .Connect("Api", "Obs", synchronous: false)
            .Connect("Db", "Obs", synchronous: false);

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        Assert.Equal(100, result.Score);
        Assert.Equal(0, result.Summary.Critical);
        Assert.Equal(0, result.Summary.Warning);
    }

    [Fact]
    public void S4_Coverage_TheAreaClimbsWithTheWiring()
    {
        static GraphBuilder Minimum() => new GraphBuilder()
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Node("Db", NodeType.Database)
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql);

        var blind = ReferenceArchitecture.Analyze(Minimum().Build());
        Assert.Equal(93, blind.ScoreOf(Area.Observability));
        Assert.Equal(2, blind.Findings.Count(f => f.Code == RuleCodes.NoObservability));

        var half = ReferenceArchitecture.Analyze(Minimum().Connect("Api", "Obs", synchronous: false).Build());
        Assert.Equal(95, half.ScoreOf(Area.Observability));
        var left = Assert.Single(half.Findings, f => f.Code == RuleCodes.NoObservability);
        Assert.Equal("Db", left.Message.Arg("node"));
        Assert.Equal(FixKinds.ConnectTo, left.Fix?.Kind);

        var whole = ReferenceArchitecture.Analyze(Minimum()
            .Connect("Api", "Obs", synchronous: false)
            .Connect("Db", "Obs", synchronous: false)
            .Build());
        Assert.Equal(100, whole.ScoreOf(Area.Observability));
        Assert.Equal(Severity.Good, Assert.Single(whole.Findings, f => f.Code == RuleCodes.NoObservability).Severity);
    }

    [Fact]
    public void B1_AllSyncCycleWithNoEntryPoint_SaysTheImpactCannotBeMeasured()
    {
        _builder
            .Nodes(NodeType.Service, "A", "B", "C")
            .Connect("A", "B", timeoutSeconds: 3).Connect("A", "C", timeoutSeconds: 3)
            .Connect("B", "A", timeoutSeconds: 3).Connect("B", "C", timeoutSeconds: 3)
            .Connect("C", "A", timeoutSeconds: 3).Connect("C", "B", timeoutSeconds: 3);

        var findings = new SpofRule().Evaluate(_builder.Build()).Findings;

        Assert.Equal(3, findings.Count);

        Assert.All(findings, f => Assert.Equal("finding.SPOF.explanation.unmeasured", f.Explanation.Key()));
    }


    [Fact]
    public void B2_TwoDifferentDatabasesWithTheSameConsumers_AreBothFlagged()
    {
        _builder
            .Nodes(NodeType.Service, "Users", "Orders")
            .Node("users-db", NodeType.Database)
            .Node("orders-db", NodeType.Database)
            .Connect("Users", "users-db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Users", "orders-db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Orders", "users-db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Orders", "orders-db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var findings = new SpofRule().Evaluate(_builder.Build()).Findings;

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(Severity.Warning, f.Severity));
        Assert.Contains(findings, f => f.Elements.Contains(_builder.NodeElement("users-db")));
        Assert.Contains(findings, f => f.Elements.Contains(_builder.NodeElement("orders-db")));

        var replicated = new GraphBuilder()
            .Nodes(NodeType.Service, "Users", "Orders")
            .Node("users-db", NodeType.Database, """{"replicas": 2}""")
            .Node("orders-db", NodeType.Database)
            .Connect("Users", "users-db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Users", "orders-db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Orders", "users-db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Orders", "orders-db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var remaining = Assert.Single(new SpofRule().Evaluate(replicated.Build()).Findings);
        Assert.Equal(replicated.NodeElement("orders-db"), Assert.Single(remaining.Elements));
    }

    [Fact]
    public void B2_BalancerInFrontOfTwoDifferentDatabases_IsNotRedundancy()
    {
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Service, "S1", "S2", "S3", "S4")
            .Node("Db1", NodeType.Database)
            .Node("Db2", NodeType.Database)
            .Connect("LB", "Db1", timeoutSeconds: 3, protocol: ProtocolType.Sql)
            .Connect("LB", "Db2", timeoutSeconds: 3, protocol: ProtocolType.Sql)
            .Connect("S1", "Db1", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("S2", "Db1", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("S3", "Db2", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("S4", "Db2", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var findings = new SpofRule().Evaluate(_builder.Build()).Findings;

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(Severity.Critical, f.Severity));
        Assert.Contains(findings, f => f.Elements.Contains(_builder.NodeElement("Db1")));
        Assert.Contains(findings, f => f.Elements.Contains(_builder.NodeElement("Db2")));
    }


    [Fact]
    public void B5_TwoObservabilityNodesWatchingEachOther_AreNotInstrumentation()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Observability, "Obs1", "Obs2")
            .Connect("Api", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql)
            .Connect("Obs1", "Obs2", synchronous: false);

        var result = new NoObservabilityRule().Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);

        Assert.Equal(["Api", "Db"], result.Findings.Select(f => f.Message.Arg("node")));
        Assert.All(result.Findings, f => Assert.Equal("finding.NO_OBSERVABILITY.unobserved.suggestion.several", f.Suggestion.Key()));
    }

    [Fact]
    public void B5_TelemetryWatchingItself_NoLongerScoresAHundred()
    {
        _builder
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Observability, "Obs1", "Obs2")
            .Connect("Api", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql)
            .Connect("Obs1", "Obs2", synchronous: false);

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        Assert.Equal(99, result.Score);
        Assert.Equal(93, result.ScoreOf(Area.Observability));
        Assert.Equal(0, result.Summary.Critical);
        Assert.Equal(2, result.Summary.Warning);
    }


    [Fact]
    public void B3_ObjectStorageOnTheFrontier_IsAnExposedDataStore()
    {
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Node("Ficheros", NodeType.ObjectStorage)
            .Connect("LB", "Ficheros", timeoutSeconds: 3);

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        var finding = Assert.Single(result.Findings, f => f.Code == RuleCodes.ExposedDatabase && f.Severity != Severity.Good);
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal(_builder.NodeElement("Ficheros"), Assert.Single(finding.Elements));

        Assert.Equal(AreaState.Scored, result.StateOf(Area.Security));
        Assert.Equal(85, result.ScoreOf(Area.Security));
    }

    [Fact]
    public void B3_ADoorToObjectStorage_IsAskedAboutAuthenticationToo()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Ficheros", NodeType.ObjectStorage)
            .Connect("Api", "Ficheros", timeoutSeconds: 3);

        var finding = Assert.Single(new UnauthenticatedEntryRule().Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.UNAUTHENTICATED_ENTRY.undeclared.message", finding.Message.Key());
        Assert.Equal("Api", finding.Message.Arg("node"));
    }

    [Fact]
    public void B4_AnOrphanQueue_IsOnlyTheOrphanFinding()
    {
        _builder
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Node("Db", NodeType.Database)
            .Node("Cola", NodeType.Queue)
            .Connect("Api", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql);

        var result = ReferenceArchitecture.Analyze(_builder.Build());
        var cola = _builder.NodeElement("Cola");

        Assert.Contains(result.Findings, f => f.Code == RuleCodes.OrphanNode && f.Elements.Contains(cola));
        Assert.DoesNotContain(result.Findings, f => f.Code == RuleCodes.QueueNoDlq && f.Severity != Severity.Good);
        Assert.Equal(100, result.ScoreOf(Area.Resilience));
        Assert.Equal(95, result.ScoreOf(Area.Coupling));
    }

    [Fact]
    public void B4_ALonelyDatabaseOnTheCanvas_IsNotASecurityIncident()
    {
        _builder.Node("Db", NodeType.Database);

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        Assert.DoesNotContain(result.Findings, f => f.Code == RuleCodes.ExposedDatabase && f.Severity != Severity.Good);
        Assert.Equal(0, result.Summary.Critical);
    }

    [Fact]
    public void B6_AWorkerPollingItsQueue_KeepsItsDatabasePrivate()
    {
        _builder
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Node("Cola", NodeType.Queue, """{"deadLetterQueue": true}""")
            .Node("Worker", NodeType.Worker)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Cola", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Worker", "Cola", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Worker", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        Assert.DoesNotContain(result.Findings, f =>
            f.Code == RuleCodes.ExposedDatabase && f.Severity != Severity.Good);
        Assert.Equal(100, result.ScoreOf(Area.Security));
    }

    [Fact]
    public void B6_ObservabilityWatchingTheDatabase_DoesNotExposeIt()
    {
        _builder
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Node("Db", NodeType.Database)
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql)
            .Connect("Obs", "Db", synchronous: false);

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        Assert.DoesNotContain(result.Findings, f =>
            f.Code == RuleCodes.ExposedDatabase && f.Severity != Severity.Good);
    }


    [Fact]
    public void S3_ADrawnClient_IsWhatDeclaresThePublicSurface()
    {
        _builder
            .Node("Web", NodeType.Client)
            .Node("Gw", NodeType.Gateway)
            .Node("AdminApi", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Web", "Gw", timeoutSeconds: 3)
            .Connect("Gw", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("AdminApi", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var findings = new UnauthenticatedEntryRule().Evaluate(_builder.Build()).Findings;

        var finding = Assert.Single(findings);
        Assert.Equal("Gw", finding.Message.Arg("node"));
        Assert.Equal("finding.UNAUTHENTICATED_ENTRY.undeclared.message", finding.Message.Key());
    }

    [Fact]
    public void S3_TheClientsCall_IsTrafficNotAConfigurableCall()
    {
        _builder
            .Node("Web", NodeType.Client)
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Node("Db", NodeType.Database)
            .Connect("Web", "Api")
            .Connect("Api", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql);

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        Assert.DoesNotContain(result.Findings, f => f.Code == RuleCodes.MissingResilience && f.Severity != Severity.Good);
        Assert.DoesNotContain(result.Findings, f => f.Code == RuleCodes.SyncChain && f.Severity != Severity.Good);
        Assert.DoesNotContain(result.Findings, f => f.Code == RuleCodes.Spof && f.Severity != Severity.Good);
        Assert.Equal(0, result.Summary.Critical);
    }

    [Fact]
    public void S3_TheCdn_AbsorbsAtTheEdge()
    {
        var bare = new GraphBuilder()
            .Node("Web", NodeType.Client, """{"requestsPerSecond": 1000}""")
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Connect("Web", "Api");

        var fronted = new GraphBuilder()
            .Node("Web", NodeType.Client, """{"requestsPerSecond": 1000}""")
            .Node("Edge", NodeType.Cdn)
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Connect("Web", "Edge")
            .Connect("Edge", "Api");

        Assert.Equal(1000m, LoadModel.Of(bare.Build()).LoadOf(bare.IdOf("Api")));
        Assert.Equal(100m, LoadModel.Of(fronted.Build()).LoadOf(fronted.IdOf("Api")));
    }

    [Fact]
    public void S3_AScheduler_IsAnInternalOrigin_AndItsJobsAreLoad()
    {
        _builder
            .Node("Cron", NodeType.Scheduler, """{"jobsPerSecond": 400}""")
            .Node("Worker", NodeType.Worker)
            .Node("Db", NodeType.Database)
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Connect("Cron", "Worker", synchronous: false)
            .Connect("Worker", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Api", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql);

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        Assert.DoesNotContain(result.Findings, f =>
            f.Code == RuleCodes.ExposedDatabase && f.Severity != Severity.Good);

        Assert.Contains(result.Findings, f =>
            f.Code == RuleCodes.UndersizedComponent
            && f.Severity == Severity.Critical
            && f.Elements.Contains(_builder.NodeElement("Worker")));
    }

    [Fact]
    public void S3_AWiredIdentityProvider_IsTheDeclaredAuthentication()
    {
        var delegating = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("IdP", NodeType.IdentityProvider)
            .Node("Db", NodeType.Database)
            .Connect("Api", "IdP", timeoutSeconds: 1)
            .Connect("Api", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var result = new UnauthenticatedEntryRule().Evaluate(delegating.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void S3_TheIdentityProvider_OnEveryRequestPath_IsTheFamousSpof()
    {
        _builder
            .Node("IdP", NodeType.IdentityProvider)
            .Nodes(NodeType.Service, "Users", "Orders", "Payments")
            .Connect("Users", "IdP", timeoutSeconds: 1)
            .Connect("Orders", "IdP", timeoutSeconds: 1)
            .Connect("Payments", "IdP", timeoutSeconds: 1);

        var finding = Assert.Single(new SpofRule().Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal(_builder.NodeElement("IdP"), Assert.Single(finding.Elements));
    }

    [Fact]
    public void B8_AnUnconfiguredExternalCall_PaysOnceInResilience()
    {
        _builder
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Node("Pagos", NodeType.ExternalService)
            .Connect("Api", "Pagos");

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        var onTheEdge = result.Findings
            .Where(f => f.Severity != Severity.Good && f.Elements.Contains(_builder.EdgeElement("Api", "Pagos")))
            .ToList();

        var external = Assert.Single(onTheEdge);
        Assert.Equal(RuleCodes.ExternalNoFallback, external.Code);
        Assert.Equal(Severity.Critical, external.Severity);
        Assert.Equal("finding.EXTERNAL_NO_FALLBACK.noTimeout.explanation", external.Explanation.Key());

        var resilience = Assert.Single(result.Findings, f => f.Code == RuleCodes.MissingResilience);
        Assert.Equal(Severity.Good, resilience.Severity);

        Assert.Equal(85, result.Areas.Single(a => a.Area == Area.Resilience).Score);
    }

    [Fact]
    public void B8_ApplyingTheExternalFix_LeavesNoWarningStandingBehindIt()
    {
        _builder
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Node("Pagos", NodeType.ExternalService)
            .Connect("Api", "Pagos");

        var before = ReferenceArchitecture.Analyze(_builder.Build());
        var fix = Assert.Single(before.Findings, f => f.Code == RuleCodes.ExternalNoFallback).Fix;

        Assert.NotNull(fix);
        Assert.Equal(5, fix!.TimeoutSeconds);
        Assert.True(fix.Fallback);

        var fixedGraph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Node("Pagos", NodeType.ExternalService)
            .Connect("Api", "Pagos", timeoutSeconds: fix.TimeoutSeconds, fallback: fix.Fallback!.Value)
            .Build();

        var after = ReferenceArchitecture.Analyze(fixedGraph);

        Assert.DoesNotContain(after.Findings, f => f.Code == RuleCodes.ExternalNoFallback && f.Severity != Severity.Good);
        Assert.DoesNotContain(after.Findings, f => f.Code == RuleCodes.MissingResilience && f.Severity != Severity.Good);
        Assert.Equal(100, after.Areas.Single(a => a.Area == Area.Resilience).Score);
    }

    [Fact]
    public void B9_SaturationShedsNothing_OnlyADeclaredMechanismDoes()
    {
        var saturated = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"authentication": true, "requestsPerSecond": 1000}""")
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var load = LoadModel.Of(saturated.Build());
        Assert.Equal(2m, load.UtilizationOf(saturated.IdOf("Api")));
        Assert.Equal(1_000m, load.LoadOf(saturated.IdOf("Db")));

        var limited = new GraphBuilder()
            .Node("Gw", NodeType.Gateway, """{"authentication": true, "requestsPerSecond": 1000, "rateLimit": 500}""")
            .Node("Db", NodeType.Database)
            .Connect("Gw", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        Assert.Equal(500m, LoadModel.Of(limited.Build()).LoadOf(limited.IdOf("Db")));
    }

    [Fact]
    public void B9_TheOverloadedCard_SaysWhatHappensToWhatIsBehind()
    {
        _builder
            .Node("Api", NodeType.Api, """{"authentication": true, "requestsPerSecond": 1000}""")
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var findings = new UndersizedComponentRule().Evaluate(_builder.Build()).Findings;

        var api = Assert.Single(findings, f => f.Elements.Contains(_builder.NodeElement("Api")));
        Assert.Equal(Severity.Critical, api.Severity);
        Assert.Equal("finding.UNDERSIZED_COMPONENT.overloaded.explanation.withDependencies", api.Explanation.Key());

        var db = Assert.Single(findings, f => f.Elements.Contains(_builder.NodeElement("Db")));
        Assert.Equal(Severity.Critical, db.Severity);
        Assert.Equal("finding.UNDERSIZED_COMPONENT.overloaded.explanation", db.Explanation.Key());
        Assert.Equal("1000", db.Explanation.Arg("loadRps"));
    }


    [Fact]
    public void S4_AQueueNobodyDrains_IsNoLongerSilent()
    {
        _builder
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Node("Cola", NodeType.Queue)
            .Connect("Api", "Cola", synchronous: false, protocol: ProtocolType.Amqp);

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        var finding = Assert.Single(result.Findings, f => f.Code == RuleCodes.QueueNoConsumer);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(95, result.Areas.Single(a => a.Area == Area.Coupling).Score);

        var dlq = Assert.Single(result.Findings, f => f.Code == RuleCodes.QueueNoDlq);
        Assert.Equal(Severity.Good, dlq.Severity);
        Assert.Equal(100, result.Areas.Single(a => a.Area == Area.Resilience).Score);
    }

    [Fact]
    public void S4_ACacheAsTheOnlyStore_IsNoLongerSilent()
    {
        _builder
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Node("Cache", NodeType.Cache)
            .Connect("Api", "Cache", timeoutSeconds: 1, fallback: true);

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        var finding = Assert.Single(result.Findings, f => f.Code == RuleCodes.CacheAsStore);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(95, result.Areas.Single(a => a.Area == Area.Resilience).Score);
        Assert.DoesNotContain(result.Findings, f => f.Code == RuleCodes.MissingCache);
    }

    [Fact]
    public void S4_TwoServicesOnOneDatabase_IsACouplingFinding_BesideTheAvailabilityOne()
    {
        _builder
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Nodes(NodeType.Service, "Users", "Orders")
            .Node("Db", NodeType.Database)
            .Connect("Api", "Users", timeoutSeconds: 3)
            .Connect("Api", "Orders", timeoutSeconds: 3)
            .Connect("Users", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Orders", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        var onDb = result.Findings
            .Where(f => f.Severity != Severity.Good && f.Elements.FirstOrDefault() == _builder.NodeElement("Db"))
            .Where(f => f.Code != RuleCodes.NoObservability)
            .ToList();

        Assert.Equal(
            [RuleCodes.Spof, RuleCodes.SharedDatabase, RuleCodes.MissingCache],
            onDb.Select(f => f.Code));
        Assert.Equal(
            [Area.Availability, Area.Coupling, Area.Scalability],
            onDb.Select(f => f.Area));

        var shared = onDb.Single(f => f.Code == RuleCodes.SharedDatabase);
        Assert.Equal(Severity.Warning, shared.Severity);
        Assert.Equal("'Users', 'Orders'", shared.Message.Arg("names"));
        Assert.Equal(95, result.Areas.Single(a => a.Area == Area.Coupling).Score);
    }

    [Fact]
    public void B7_ASelfLoop_IsNotItsOwnDependent()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Recursivo", NodeType.Service)
            .Connect("Api", "Recursivo", timeoutSeconds: 3)
            .Connect("Recursivo", "Recursivo", timeoutSeconds: 3);

        var result = new SpofRule().Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);

        var cycle = Assert.Single(new CycleRule().Evaluate(_builder.Build()).Findings);
        Assert.Equal(Severity.Critical, cycle.Severity);
    }


    [Fact]
    public void A2_ARaisedTimeout_IsNotTheAnswerToADrowningDatabase()
    {
        _builder
            .Node("Users", NodeType.Client, """{"requestsPerSecond": 50000}""")
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Node("Db", NodeType.Database)
            .Connect("Users", "Api")
            .Connect("Api", "Db", timeoutSeconds: 3, protocol: ProtocolType.Sql);

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        var timeout = Assert.Single(result.Findings, f => f.Code == RuleCodes.TimeoutTooShort);
        Assert.Equal(Severity.Good, timeout.Severity);

        Assert.Contains(result.Findings, f =>
            f.Code == RuleCodes.UndersizedComponent
            && f.Severity == Severity.Critical
            && f.Elements.Contains(_builder.NodeElement("Db")));
    }

    [Fact]
    public void A2_BelowTheLine_TheTimeoutIsStillTheLesson()
    {
        _builder
            .Node("Api", NodeType.Api, """{"authentication": true, "requestsPerSecond": 285}""")
            .Node("Svc", NodeType.Service)
            .Node("Stripe", NodeType.ExternalService)
            .Connect("Api", "Svc", timeoutSeconds: 1)
            .Connect("Svc", "Stripe", timeoutSeconds: 5, fallback: true);

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        var timeout = Assert.Single(result.Findings, f => f.Code == RuleCodes.TimeoutTooShort);
        Assert.Equal(Severity.Critical, timeout.Severity);
        Assert.Equal(_builder.EdgeElement("Api", "Svc"), Assert.Single(timeout.Elements));

        Assert.DoesNotContain(result.Findings, f => f.Code == RuleCodes.UndersizedComponent && f.Severity == Severity.Critical);
        Assert.Contains(result.Findings, f => f.Code == RuleCodes.UndersizedComponent && f.Severity == Severity.Warning);
    }

    [Fact]
    public void A2_TheOrderTheTwoRulesAreRegisteredIn_DoesNotMatter()
    {
        _builder
            .Node("Users", NodeType.Client, """{"requestsPerSecond": 50000}""")
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Node("Db", NodeType.Database)
            .Connect("Users", "Api")
            .Connect("Api", "Db", timeoutSeconds: 3, protocol: ProtocolType.Sql);

        var forwards = new ArchitectureAnalyzer([new TimeoutTooShortRule(), new UndersizedComponentRule()]).Analyze(_builder.Build());
        var backwards = new ArchitectureAnalyzer([new UndersizedComponentRule(), new TimeoutTooShortRule()]).Analyze(_builder.Build());

        Assert.Equal(Describe(forwards), Describe(backwards));
        Assert.DoesNotContain(forwards.Findings, f => f.Code == RuleCodes.TimeoutTooShort && f.Severity != Severity.Good);
    }

    private static string Describe(AnalysisResult result) => string.Join("\n", result.Findings
        .Select(f => $"{f.Severity} {f.Area} {f.Code} {f.Message.Describe()} {f.Explanation.Describe()}"));
}
