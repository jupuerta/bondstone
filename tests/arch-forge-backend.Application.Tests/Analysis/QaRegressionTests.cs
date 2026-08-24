using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis;

/// <summary>
/// The QA sweep of 2026-08-23 (docs/QA-analisis.md), as regression tests. They started life as
/// characterizations - every bug pinned as it behaved, with a <c>BUG</c> marker naming the
/// expected fix - and were flipped to the fixed behaviour in the same commit as each fix, which
/// is how B1 through B7 all landed under scoring v6 without one fix silently moving another.
///
/// <strong>The flip discipline still applies</strong> to whatever the next sweep finds: pin the
/// wrong behaviour first, mark the assertion a fix is expected to break, and never delete a test -
/// flip it. Bug ids (B1, B2, ...) and section ids (S3_*) are the sections of docs/QA-analisis.md.
/// </summary>
public class QaRegressionTests
{
    private readonly GraphBuilder _builder = new();

    // ---------------------------------------------------------------------------------
    // B1 - FIXED (scoring v6). Pattern B's severity now comes from the same measurement
    //      its explanation prints, and Observability stays out of the pattern. These
    //      three pin the fix; they are no longer characterizations of a bug.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void B1_AsyncPipeline_GradesThePassageByWhatItsLossActuallyCosts()
    {
        // The pipeline this product itself recommends: hand the work to a queue and answer.
        _builder
            .Node("Api", NodeType.Api)
            .Nodes(NodeType.Queue, "Cola1", "Cola2", "Cola3")
            .Node("Worker", NodeType.Worker)
            .Connect("Api", "Cola1", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Cola1", "Cola2", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Cola2", "Cola3", synchronous: false, protocol: ProtocolType.Amqp)
            .Connect("Cola3", "Worker", synchronous: false, protocol: ProtocolType.Amqp);

        var findings = new SpofRule().Evaluate(_builder.Build()).Findings;

        // The mid-pipeline queue is still a point of passage worth a note - one broker, no
        // cluster - but losing it delays work and fails no request, so it is a Warning whose
        // card and severity say the same thing. Before v6 it was a Critical whose own
        // explanation said not one request is lost.
        var cola1 = Assert.Single(findings, f => f.Elements.Contains(_builder.NodeElement("Cola1")));
        Assert.Equal(Severity.Warning, cola1.Severity);
        Assert.Equal("finding.SPOF.unreachable.message", cola1.Message.Key());
        Assert.Equal("finding.SPOF.explanation.covered", cola1.Explanation.Key());

        // The door, in contrast, costs everything that enters, and stays the honest Critical.
        var api = Assert.Single(findings, f => f.Elements.Contains(_builder.NodeElement("Api")));
        Assert.Equal(Severity.Critical, api.Severity);
        Assert.Equal("finding.SPOF.explanation.measured", api.Explanation.Key());
    }

    [Fact]
    public void B1_ConnectingObservability_DoesNotChangeTheSpofOutcome()
    {
        // Without telemetry: one door, one database behind it. Pattern B sees a single node
        // behind the Api, which is under its threshold, and stays quiet.
        var bare = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql);

        Assert.Empty(new SpofRule().Evaluate(bare.Build()).Findings);

        // The same architecture after doing the right thing - wiring an Observability node,
        // asynchronously, exactly as NO_OBSERVABILITY asks. Before v6 that edge pushed the
        // "unreachable" count to 2 and Availability paid 15 points for adding telemetry; the
        // telemetry box does not count among what falls (the doctrine CapacityModel already
        // applies to its capacity: it is nobody's lesson).
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
        // The full twenty rules over the diagram that does everything this product asks:
        // authenticated door, protected call, telemetry wired to *both* boxes. Before v6 it scored
        // 98 with a Critical on the Api - doing the right thing was what created the SPOF. And
        // since QA §4 "telemetry wired" means every component, not some component: see the
        // sibling test for the one wire short.
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
        // QA §4: "el área Observability solo puede valer 100/95 (una regla, binaria)". Now it is a
        // climb: nothing wired, the Api wired, both wired - 93, 95, 100 - and the card at each
        // step names the box still running blind.
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
        // Three services fully connected, every hop synchronous, nothing protected, and no root
        // anywhere - the graph is all cycle.
        _builder
            .Nodes(NodeType.Service, "A", "B", "C")
            .Connect("A", "B", timeoutSeconds: 3).Connect("A", "C", timeoutSeconds: 3)
            .Connect("B", "A", timeoutSeconds: 3).Connect("B", "C", timeoutSeconds: 3)
            .Connect("C", "A", timeoutSeconds: 3).Connect("C", "B", timeoutSeconds: 3);

        var findings = new SpofRule().Evaluate(_builder.Build()).Findings;

        Assert.Equal(3, findings.Count);

        // OutageImpact answers 0 here because there is no entry traffic to measure, and before
        // v6 the rule read every zero as "everything that calls it does so async or with a
        // fallback" - false on all six edges of this graph. The unmeasured sentence claims
        // nothing it cannot see.
        Assert.All(findings, f => Assert.Equal("finding.SPOF.explanation.unmeasured", f.Explanation.Key()));
    }

    // ---------------------------------------------------------------------------------
    // B2 - FIXED (scoring v6). A system of record's redundancy is declared (replicas on
    //      the component), never inferred from siblings or balancers. These pin the fix.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void B2_TwoDifferentDatabasesWithTheSameConsumers_AreBothFlagged()
    {
        // users-db and orders-db are two truths, not two copies: each one is a hard, unprotected
        // dependency of both services, and losing either loses half the product. Before v6 the
        // equivalent-sibling exemption read "same type, same consumers" as "replicas" and said
        // nothing about either.
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

        // And the way out is the one the suggestion names: declare the replicas. The same graph
        // with users-db replicated keeps only the honest finding.
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
        // Each database has three hard dependents of its own. Before v6, "hangs from a
        // LoadBalancer that spreads over ≥2 nodes of its type" exempted both - but balancing over
        // two *different* databases is routing writes to two primaries, and it earns no exemption
        // for them nor for the balancer itself.
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

    // ---------------------------------------------------------------------------------
    // B5 - FIXED (scoring v6). Instrumentation is an edge between an Observability node
    //      and something that is not telemetry; watching the watcher observes nothing.
    // ---------------------------------------------------------------------------------

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

        // Before v6 the rule asked whether some Observability node had some edge, and Obs1 -> Obs2
        // satisfied it while the actual system ran blind. With coverage (QA §4) the question is
        // asked per component, and neither box of the system has a wire to either telemetry: two
        // findings, and the two telemetry boxes count for nothing between themselves.
        Assert.Equal(["Api", "Db"], result.Findings.Select(f => f.Message.Arg("node")));
        Assert.All(result.Findings, f => Assert.Equal("finding.NO_OBSERVABILITY.unobserved.suggestion.several", f.Suggestion.Key()));
    }

    [Fact]
    public void B5_TelemetryWatchingItself_NoLongerScoresAHundred()
    {
        // The full twenty rules, over the nonsense variant of the well-configured minimum.
        // Before v6 this was the only perfect score of the whole QA sweep; the honest wiring
        // (telemetry connected to the Api) is the one that reaches 100 now - see
        // B1_TheWellConfiguredMinimum_WithTelemetry_ReachesAHundred.
        _builder
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Observability, "Obs1", "Obs2")
            .Connect("Api", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql)
            .Connect("Obs1", "Obs2", synchronous: false);

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        // Two unobserved components: 5 + 2,5 on the area (D1.1), and 99 overall.
        Assert.Equal(99, result.Score);
        Assert.Equal(93, result.ScoreOf(Area.Observability));
        Assert.Equal(0, result.Summary.Critical);
        Assert.Equal(2, result.Summary.Warning);
    }

    // ---------------------------------------------------------------------------------
    // B3 + B4 + B6 + B7 - FIXED (scoring v6).
    // ---------------------------------------------------------------------------------

    [Fact]
    public void B3_ObjectStorageOnTheFrontier_IsAnExposedDataStore()
    {
        // The textbook public bucket: traffic meets the files without anything that could ask who
        // is calling. Before v6 both Security rules asked for NodeType.Database specifically, so
        // this graph left the whole area without a score; they now ask IsSystemOfRecord, which is
        // the question they meant.
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
        // The other half of the same blindness: an Api with undeclared authentication in front of
        // the files was not even a question before v6, because "data behind the door" meant a
        // Database and nothing else.
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
        // FIXED (scoring v6). ORPHAN_NODE's "every other rule will simply ignore it" is now kept:
        // the orphan set lives on the graph and the node-scoped rules (QUEUE_NO_DLQ,
        // EXPOSED_DATABASE, NO_RATE_LIMIT, UNDERSIZED_COMPONENT) read it.
        _builder
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Node("Db", NodeType.Database)
            .Node("Cola", NodeType.Queue)
            .Connect("Api", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql);

        var result = ReferenceArchitecture.Analyze(_builder.Build());
        var cola = _builder.NodeElement("Cola");

        // The leftover box is said once, in Coupling, and Resilience no longer pays for a DLQ on
        // a queue that is part of no flow.
        Assert.Contains(result.Findings, f => f.Code == RuleCodes.OrphanNode && f.Elements.Contains(cola));
        Assert.DoesNotContain(result.Findings, f => f.Code == RuleCodes.QueueNoDlq && f.Severity != Severity.Good);
        Assert.Equal(100, result.ScoreOf(Area.Resilience));
        Assert.Equal(95, result.ScoreOf(Area.Coupling));
    }

    [Fact]
    public void B4_ALonelyDatabaseOnTheCanvas_IsNotASecurityIncident()
    {
        // The worst case the old behaviour allowed: a single Database node - the first thing
        // somebody drags in - opened as EXPOSED_DATABASE Critical, because a node with no edges is
        // its own root and therefore sat on the frontier. One box is a sketch, not a breach.
        _builder.Node("Db", NodeType.Database);

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        Assert.DoesNotContain(result.Findings, f => f.Code == RuleCodes.ExposedDatabase && f.Severity != Severity.Good);
        Assert.Equal(0, result.Summary.Critical);
    }

    [Fact]
    public void B6_AWorkerPollingItsQueue_KeepsItsDatabasePrivate()
    {
        // FIXED (scoring v6). Drawing the worker as the one who depends on the queue is a
        // perfectly reasonable way to draw polling - and before the fix it made the Worker a
        // root, the frontier read every root as "outside", and the worker's private database
        // came out flagged as open to the world. A root that acts from inside (Worker,
        // Observability, Scheduler) is no longer a door.
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
        // The same conflation from the telemetry side: "Obs -> Db" is somebody drawing "I watch
        // the database", and it used to put the database on the public frontier.
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

    // ---------------------------------------------------------------------------------
    // QA §3 - the four types of the QA round: Client, Cdn, Scheduler, IdentityProvider.
    // Each test is the mechanism that earns the type its place under V2 D3.2.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void S3_ADrawnClient_IsWhatDeclaresThePublicSurface()
    {
        // With a Client on the canvas, "public" stops being inferred from the shape: the gateway
        // the users call is asked about authentication, and the admin Api that hangs from nothing
        // is by declaration internal - no Client points at it.
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
        // The users' edge carries no timeout and never will - you do not configure somebody's
        // browser - so drawing a Client must not open the diagram with resilience advice about
        // it, count it as a link of the chain, or crown the users a single point of failure.
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
        // The lesson the type exists to teach, measured: the same thousand users, a tenth of them
        // reaching the origin. Without the Cdn the Api drowns; with it, nothing does.
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
        // The clock gives the Worker a drawn trigger - so neither of them is a door (B6) - and
        // its jobs flow into the load model, which is what lets the analyzer say the batch is
        // drowning its worker before anybody simulates anything.
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

        // 400 jobs against a Worker that takes 200: the batch is an outage in progress.
        Assert.Contains(result.Findings, f =>
            f.Code == RuleCodes.UndersizedComponent
            && f.Severity == Severity.Critical
            && f.Elements.Contains(_builder.NodeElement("Worker")));
    }

    [Fact]
    public void S3_AWiredIdentityProvider_IsTheDeclaredAuthentication()
    {
        // Delegation is a declaration: the door that hands the "who are you" question to an IdP
        // has answered it, checkbox or no checkbox.
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
        // No special case buys this: three services blocking on one IdP is pattern A doing its
        // job, and it is the most famous outage in the industry - which is why the type is worth
        // having on the palette at all.
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
        // FIXED (scoring v6). The bare call to a third party used to collect EXTERNAL_NO_FALLBACK
        // (Critical, 15) *and* MISSING_RESILIENCE (Warning, 5) on the same edge - two codes
        // scolding you for not protecting one call, which is the noise D4.5 exists to avoid.
        // Resilience read 80; it reads 85 now, and the one finding left names both halves.
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

        // The sibling rule still ran - it simply has nothing left to say about this edge.
        var resilience = Assert.Single(result.Findings, f => f.Code == RuleCodes.MissingResilience);
        Assert.Equal(Severity.Good, resilience.Severity);

        Assert.Equal(85, result.Areas.Single(a => a.Area == Area.Resilience).Score);
    }

    [Fact]
    public void B8_ApplyingTheExternalFix_LeavesNoWarningStandingBehindIt()
    {
        // The fix of the absorbed variant sets the timeout with the fallback. Had it set only the
        // fallback, clearing the Critical would have uncovered the very Warning it used to hide.
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
        // DECIDED (scoring v6), not fixed: the QA read "what does not fit is rejected" on the Api's
        // card and 1.000 rps on the database behind it, and asked which of the two was true. The
        // model is: a saturated component passes its whole load on, because being over capacity
        // is not a rate limit - nothing turns the requests away, they come in, slow down and fail
        // after they have already hit the dependencies. Shedding is what a *declared* mechanism
        // does, and that is what NO_RATE_LIMIT teaches. The sentence changed; the propagation
        // did not.
        var saturated = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"authentication": true, "requestsPerSecond": 1000}""")
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var load = LoadModel.Of(saturated.Build());
        Assert.Equal(2m, load.UtilizationOf(saturated.IdOf("Api")));   // 1.000 over 500
        Assert.Equal(1_000m, load.LoadOf(saturated.IdOf("Db")));       // all of it, not the 500 served

        // The same thousand through a gateway that admits five hundred: the limit is the thing
        // that protects what is behind, which is why it is worth a rule of its own.
        var limited = new GraphBuilder()
            .Node("Gw", NodeType.Gateway, """{"authentication": true, "requestsPerSecond": 1000, "rateLimit": 500}""")
            .Node("Db", NodeType.Database)
            .Connect("Gw", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        Assert.Equal(500m, LoadModel.Of(limited.Build()).LoadOf(limited.IdOf("Db")));
    }

    [Fact]
    public void B9_TheOverloadedCard_SaysWhatHappensToWhatIsBehind()
    {
        // Both bottlenecks show at once - that was always the pedagogical point - and now the
        // Api's card explains why the database's is there too. The database, with nothing behind
        // it, gets the sentence without the clause.
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

    // ---------------------------------------------------------------------------------
    // QA §4 - the three cheap rules. Each test is the scenario the QA ran and got silence
    // from, now under the full analyzer.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void S4_AQueueNobodyDrains_IsNoLongerSilent()
    {
        // "Api → Cola y nadie a quien la cola entregue: los mensajes entran y no salen. Hoy es
        // invisible (probado: silencio total)." Coupling pays the Warning; QUEUE_NO_DLQ waits for
        // the consumer before asking about the dead-letter queue, so the box carries one finding.
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
        // "Api que solo escribe en Cache, sin sistema de registro en el grafo (probado: silencio,
        // 96 puntos)." The call itself is protected, so the only thing Resilience has to say is
        // that the data has nowhere to come back from.
        _builder
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Node("Cache", NodeType.Cache)
            .Connect("Api", "Cache", timeoutSeconds: 1, fallback: true);

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        var finding = Assert.Single(result.Findings, f => f.Code == RuleCodes.CacheAsStore);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(95, result.Areas.Single(a => a.Area == Area.Resilience).Score);
        Assert.DoesNotContain(result.Findings, f => f.Code == RuleCodes.MissingCache);   // no database: not applicable
    }

    [Fact]
    public void S4_TwoServicesOnOneDatabase_IsACouplingFinding_BesideTheAvailabilityOne()
    {
        // "Dos Services escribiendo en la misma Db es el anti-patrón de acoplamiento clásico.
        // Hoy solo sale como MISSING_CACHE (que habla de lecturas)." Three areas now read the
        // same box and say three different things: SPOF that it falls with two services on it,
        // MISSING_CACHE that the reads have no cache, SHARED_DATABASE that the schema is the
        // interface between them. None of the three is the other counted twice.
        _builder
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Nodes(NodeType.Service, "Users", "Orders")
            .Node("Db", NodeType.Database)
            .Connect("Api", "Users", timeoutSeconds: 3)
            .Connect("Api", "Orders", timeoutSeconds: 3)
            .Connect("Users", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Orders", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        // The findings whose subject is the database - first element - as opposed to the chain
        // that merely ends at it.
        var onDb = result.Findings
            .Where(f => f.Severity != Severity.Good && f.Elements.FirstOrDefault() == _builder.NodeElement("Db"))
            .Where(f => f.Code != RuleCodes.NoObservability)   // the coverage card is about the wiring, not the box
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
        // FIXED (scoring v6). The self-edge used to count Recursivo among its own hard
        // dependents, turning one real consumer into the two the threshold asks for. A node
        // calling itself is CYCLE's finding - and only CYCLE's.
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

    // ---------------------------------------------------------------------------------
    // V15 A2 (docs/V15/QA-analisis-V15.md, C7). Under the full analyzer on purpose: what
    // is being tested is the two phases - UNDERSIZED_COMPONENT publishes, TIMEOUT_TOO_SHORT
    // yields - not either rule on its own.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void A2_ARaisedTimeout_IsNotTheAnswerToADrowningDatabase()
    {
        // S4: "Users(50.000 rps) → Api → Db, timeout de 3 s". Before: «Cuesta 33,34 s con todo
        // funcionando … Sube el timeout por encima de 33,34 s», guided fix 67 s, two cards above
        // the two UNDERSIZED_COMPONENT criticals saying the opposite - Resilience paying 15 for
        // what Scalability had already charged. (Those are the QA's panel figures; this graph, with
        // every one of the 50.000 requests reaching the database, read 100 s and offered 201 s.)
        // Now the only cards about that call name the cause.
        _builder
            .Node("Users", NodeType.Client, """{"requestsPerSecond": 50000}""")
            .Node("Api", NodeType.Api, """{"authentication": true}""")
            .Node("Db", NodeType.Database)
            .Connect("Users", "Api")
            .Connect("Api", "Db", timeoutSeconds: 3, protocol: ProtocolType.Sql);

        var result = ReferenceArchitecture.Analyze(_builder.Build());

        // Applicable and clean - the timeout is doing its job - rather than not applicable.
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
        // The control in negative. Svc and Stripe at 95 %: nothing overloaded, so no Critical from
        // UNDERSIZED_COMPONENT and nothing for TIMEOUT_TOO_SHORT to yield to - and a 1 s timeout on
        // a call that costs 1,2 s with every box still answering is exactly its finding. (At 80 %
        // a single hop costs 102 ms and no valid timeout is shorter than that: the queueing factor
        // tops out at 20 just under the line, so the control lives there, on a chain.)
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
        // Moving a rule from Consumer to Producer changes when it runs, and D5.2 says the order
        // it was *registered* in must not matter. Both orders, same graph, same answer - and the
        // answer is the yielded one.
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
