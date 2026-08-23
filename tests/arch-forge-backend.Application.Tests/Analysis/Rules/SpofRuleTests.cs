using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

public class SpofRuleTests
{
    private readonly SpofRule _rule = new();
    private readonly GraphBuilder _builder = new();

    [Fact]
    public void PatternA_ThreeDependents_IsCritical()
    {
        _builder
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Service, "Orders", "Payments", "Users")
            .Connect("Orders", "Db").Connect("Payments", "Db").Connect("Users", "Db");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(RuleCodes.Spof, finding.Code);
        Assert.Equal(Area.Availability, finding.Area);
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.SPOF.shared.message(count=3, node=Db)", finding.Message.Describe());
        Assert.Equal(_builder.NodeElement("Db"), Assert.Single(finding.Elements));
    }

    [Fact]
    public void PatternA_ExactlyTwoDependents_IsWarning()
    {
        // Severity by blast radius (D4.x): two dependents is not the same failure as ten.
        _builder
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Db").Connect("Payments", "Db");

        Assert.Equal(Severity.Warning, Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Severity);
    }

    [Fact]
    public void PatternA_SingleDependent_PassesInsteadOfFiring()
    {
        // The rule had something to check and it was fine - a green check, not silence (D1.4).
        // Were this "not applicable", Availability could never reach a clean 100.
        _builder
            .Node("Db", NodeType.Database)
            .Node("Api", NodeType.Api)
            .Connect("Api", "Db");

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void PatternB_GatewayWithNoDependents_IsCritical()
    {
        // The flagship example (§18, Post 4): nobody points at a gateway, so pattern A sees
        // nothing. Remove it and everything behind it becomes unreachable.
        _builder
            .Node("Gateway", NodeType.Gateway)
            .Nodes(NodeType.Service, "Users", "Orders", "Payments")
            .Connect("Gateway", "Users").Connect("Gateway", "Orders").Connect("Gateway", "Payments");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.SPOF.unreachable.message", finding.Message.Key());
        Assert.Equal("3", finding.Message.Arg("count"));
        Assert.Equal(_builder.NodeElement("Gateway"), Assert.Single(finding.Elements));
    }

    [Fact]
    public void PatternB_OnlyOneNodeBehindIt_DoesNotFire()
    {
        _builder
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void PatternB_AlternativePathKeepsEverythingReachable_DoesNotFire()
    {
        // Api1 is not a point of passage: without it, Cache and Queue are still reachable via Api2.
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Node("Cache", NodeType.Cache)
            .Node("Queue", NodeType.Queue)
            .Connect("LB", "Api1").Connect("LB", "Api2")
            .Connect("Api1", "Cache").Connect("Api2", "Cache")
            .Connect("Api1", "Queue").Connect("Api2", "Queue");

        var findings = _rule.Evaluate(_builder.Build()).Findings;

        Assert.DoesNotContain(findings, f => f.Elements.Contains(_builder.NodeElement("Api1")));
    }

    [Fact]
    public void BothPatterns_ReportOneFindingWithTheHighestSeverity()
    {
        // Orders has two dependents (pattern A, Warning) and is the only way through to what is
        // behind it (pattern B, Critical). One finding per node, the worse severity (D4.x).
        _builder
            .Node("Gateway", NodeType.Gateway)
            .Nodes(NodeType.Service, "Orders", "Payments", "Inventory")
            .Connect("Gateway", "Orders")
            .Connect("Orders", "Payments")
            .Connect("Payments", "Inventory")
            .Connect("Inventory", "Orders");

        var findings = _rule.Evaluate(_builder.Build()).Findings;

        var orders = Assert.Single(findings, f => f.Elements.Contains(_builder.NodeElement("Orders")));
        Assert.Equal(Severity.Critical, orders.Severity);
        Assert.Equal("finding.SPOF.unreachable.message", orders.Message.Key());
    }

    [Fact]
    public void LoadBalancerSpreadingOverTwoNodesOfTheSameType_IsNeverFlagged()
    {
        // Without this exception the model loops: you add a balancer to fix a SPOF and the
        // balancer becomes the SPOF, with no way out (D4.x).
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Connect("LB", "Api1").Connect("LB", "Api2");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void LoadBalancerWithASingleTarget_IsNotExempt()
    {
        // It balances nothing, so it is a point of passage like any other.
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("LB", "Api").Connect("Api", "Db");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(_builder.NodeElement("LB"), Assert.Single(finding.Elements));
    }

    [Fact]
    public void ExplicitRedundancy_NodeBehindABalancer_IsExempt()
    {
        // Api1 and Api2 each serve both consumers, and the balancer is what makes them
        // interchangeable.
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("LB", "Api1").Connect("LB", "Api2")
            .Connect("Orders", "Api1").Connect("Orders", "Api2")
            .Connect("Payments", "Api1").Connect("Payments", "Api2");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void EquivalentSibling_SameTypeAndSameDependents_IsNotARedundancyClaim()
    {
        // Flipped in V14 B11. This used to pin the "equivalent sibling" exemption: two Services
        // serving exactly the same consumers were taken for interchangeable copies. QA B2 had
        // already removed that for systems of record with an argument that applied word for word
        // here - they pass every topological test and are still two things, not two copies - and
        // B4 gave the field that says "two" on purpose. So two boxes of one type with two hard
        // dependents each are two Warnings, and the way to say they are replicas is a balancer
        // over them or `instances: 2` on one of them. See TwoBusinessServices_SharingTheirCallers.
        _builder
            .Nodes(NodeType.Service, "Search1", "Search2")
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Connect("Api1", "Search1").Connect("Api1", "Search2")
            .Connect("Api2", "Search1").Connect("Api2", "Search2");

        var findings = _rule.Evaluate(_builder.Build()).Findings;

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(Severity.Warning, f.Severity));
    }

    [Fact]
    public void ASystemOfRecord_IsClearedByDeclaredReplicasAlone()
    {
        // The loop this closes (QA B2): the suggestion says "add replicas with failover", the UI
        // edits the field, and now the rule reads it. Read replicas deliberately do not clear it -
        // they spread reads and can never take the write, which is the whole asymmetry of D1.1.
        var replicated = new GraphBuilder()
            .Node("Db", NodeType.Database, """{"replicas": 2}""")
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Db").Connect("Payments", "Db");

        Assert.Empty(_rule.Evaluate(replicated.Build()).Findings);

        var readOnly = new GraphBuilder()
            .Node("Db", NodeType.Database, """{"readReplicas": 3}""")
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Db").Connect("Payments", "Db");

        Assert.Single(_rule.Evaluate(readOnly.Build()).Findings);
    }

    // ---- V14 B4: redundancy you can say, not only redundancy you can draw ----

    [Fact]
    public void AStatelessComponent_IsClearedByDeclaredInstances()
    {
        // The other half of the doctrine QA B2 wrote for the system of record. The card handed to
        // an Api says "add a second equivalent instance", the inspector edits that field, the node
        // prints "2 inst" on its own face and CapacityModel has doubled its capacity for it since
        // V4 - and this rule was the single place in the product where it meant nothing, so an Api
        // with ten instances was told, in a Critical, to add a second one.
        var declared = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"instances": 2}""")
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Api").Connect("Payments", "Api");

        Assert.Empty(_rule.Evaluate(declared.Build()).Findings);

        // And one instance is the same architecture with nothing declared: still a SPOF.
        var single = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"instances": 1}""")
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Api").Connect("Payments", "Api");

        Assert.Single(_rule.Evaluate(single.Build()).Findings);
    }

    [Theory]
    [InlineData(NodeType.Api)]
    [InlineData(NodeType.Service)]
    [InlineData(NodeType.Worker)]
    public void EveryTypeThatOwnsTheField_IsClearedByIt(NodeType type)
    {
        // Three types declare instances and all three are read, because the bug was never about
        // the Api - it was about the field meaning one thing to the load model and nothing here.
        _builder
            .Node("Target", type, """{"instances": 3}""")
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Target").Connect("Payments", "Target");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Theory]
    [InlineData("""{"instances": 0}""")]
    [InlineData("""{"instances": -3}""")]
    [InlineData("""{"instances": 1}""")]
    [InlineData("{}")]
    public void InstancesThatDoNotAmountToTwo_AreNotADeclaration(string configuration)
    {
        // Zero and a negative are not a smaller redundancy, they are no redundancy - and the
        // schema lets them through today (V14 B14), so the rule guards its own reading.
        _builder
            .Node("Api", NodeType.Api, configuration)
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Api").Connect("Payments", "Api");

        Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void DeclaringInstancesOnATypeThatHasNoSuchField_ClearsNothing()
    {
        // A Queue's redundancy is a clustered broker, not a count - which is what its own
        // suggestion says. Writing the field anyway is configuration for another schema and gets
        // the answer that deserves (V14 C6): the finding stands.
        _builder
            .Node("Queue", NodeType.Queue, """{"instances": 5}""")
            .Nodes(NodeType.Api, "Orders", "Payments")
            .Connect("Orders", "Queue").Connect("Payments", "Queue");

        Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void TheDeclaredFrontDoor_IsNoLongerAPointOfPassage()
    {
        // U8, the reproduction in the sweep: Api and Svc with ten instances each, the telemetry
        // wired as NO_OBSERVABILITY asks, the database replicated - everything the product teaches,
        // declared - and SPOF still called the Api a critical single point of failure and told the
        // student to add the second instance that was already the tenth. Pattern B stops at the
        // declaration too: a component there are ten of is not the one thing whose loss cuts the
        // graph in half.
        _builder
            .Node("Api", NodeType.Api, """{"instances": 10}""")
            .Node("Svc", NodeType.Service, """{"instances": 10}""")
            .Node("Db", NodeType.Database, """{"replicas": 2}""")
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Svc", timeoutSeconds: 5, fallback: true)
            .Connect("Svc", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql)
            .Connect("Obs", "Api", synchronous: false)
            .Connect("Obs", "Svc", synchronous: false)
            .Connect("Obs", "Db", synchronous: false);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void ADeclaredComponentStillCountsAsSomebodyElsesDependent()
    {
        // The exemption is about this node's own redundancy and nothing else: ten copies of the
        // Api do not make the single database behind it any less of one.
        _builder
            .Node("Db", NodeType.Database)
            .Node("Api1", NodeType.Api, """{"instances": 4}""")
            .Node("Api2", NodeType.Api, """{"instances": 4}""")
            .Connect("Api1", "Db").Connect("Api2", "Db");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(_builder.NodeElement("Db"), Assert.Single(finding.Elements));
    }

    [Fact]
    public void SiblingsOfTheSameTypeServingDifferentConsumers_AreNotRedundant()
    {
        // V1 asked only for "some dependent in common", which with microservices exempted Users,
        // Orders and Payments from each other for hanging off the same gateway (D4.x). Stateless
        // types, because since QA B2 they are the only ones the sibling exemption applies to.
        _builder
            .Nodes(NodeType.Service, "Search", "Reporting")
            .Nodes(NodeType.Api, "Orders", "Payments", "Users")
            .Connect("Orders", "Search").Connect("Orders", "Reporting")
            .Connect("Payments", "Search").Connect("Users", "Search");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(_builder.NodeElement("Search"), Assert.Single(finding.Elements));
    }

    // ---- V13 D1.1: an arrow is not a dependency until you read it -------------

    [Fact]
    public void AsynchronousDependents_DoNotCount()
    {
        // The two APIs post to the queue and answer. The queue being down delays the work; it does
        // not fail their requests, so they are not components that fall with it.
        _builder
            .Node("Queue", NodeType.Queue)
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Connect("Api1", "Queue", synchronous: false)
            .Connect("Api2", "Queue", synchronous: false);

        var result = _rule.Evaluate(_builder.Build());

        // Applicable, not silent: there were arrows to read and they came out clean (D1.4).
        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void DependentsHoldingAFallback_DoNotCount()
    {
        // A caller with a declared fallback has somewhere else to go. This is the Cache of
        // resilient-async, which was a Warning for three versions for being used correctly.
        _builder
            .Node("Cache", NodeType.Cache)
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Connect("Api1", "Cache", timeoutSeconds: 1, fallback: true)
            .Connect("Api2", "Cache", timeoutSeconds: 1, fallback: true);

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void AFallbackInFrontOfTheSystemOfRecord_StillCounts()
    {
        // The one exception, and the reason this is not "trust the fallback" (D1.1): a fallback in
        // front of a database serves something stale or refuses. Neither is the write that was lost.
        _builder
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Db", circuitBreaker: true, fallback: true)
            .Connect("Payments", "Db", circuitBreaker: true, fallback: true);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("finding.SPOF.shared.message(count=2, node=Db)", finding.Message.Describe());
    }

    [Fact]
    public void ObjectStorage_IsASystemOfRecordToo()
    {
        // The type V2 D3.2 said had to earn its place and never had: no rule read ObjectStorage
        // until this one did. Files are as unrecoverable by a fallback as rows are.
        _builder
            .Node("Files", NodeType.ObjectStorage)
            .Nodes(NodeType.Service, "Orders", "Invoices")
            .Connect("Orders", "Files", fallback: true)
            .Connect("Invoices", "Files", fallback: true);

        Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void ACircuitBreakerOnItsOwn_IsNotProtection()
    {
        // It stops the caller hammering something already down and it stops the cascade. It does
        // not produce an answer, so the caller still falls (D1.1).
        _builder
            .Node("Payments", NodeType.Service)
            .Nodes(NodeType.Service, "Orders", "Checkout")
            .Connect("Orders", "Payments", circuitBreaker: true)
            .Connect("Checkout", "Payments", circuitBreaker: true);

        Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void MixedEdges_CountOnlyTheHardOnes()
    {
        // Three dependents would be Critical; only two of them block. Severity is graded on what
        // actually falls, which is what stops MISSING_RESILIENCE escalating off a miscount (D4.4).
        _builder
            .Node("Payments", NodeType.Service)
            .Nodes(NodeType.Service, "Orders", "Checkout", "Reporting")
            .Connect("Orders", "Payments")
            .Connect("Checkout", "Payments")
            .Connect("Reporting", "Payments", synchronous: false);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("2", finding.Message.Arg("count"));
    }

    [Fact]
    public void EveryWayInBeingSoft_DoesNotStopPatternB()
    {
        // Pattern B is about passage, not about who falls: with the gateway gone, three components
        // are unreachable however politely they were being called (D1.1, scope).
        _builder
            .Node("Gateway", NodeType.Gateway)
            .Nodes(NodeType.Service, "Users", "Orders", "Payments")
            .Connect("Gateway", "Users", fallback: true)
            .Connect("Gateway", "Orders", fallback: true)
            .Connect("Gateway", "Payments", fallback: true);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("finding.SPOF.unreachable.message", finding.Message.Key());
    }

    // ---- V14 B1: the walk follows traffic, and telemetry is not traffic ------

    [Fact]
    public void WiringTheTelemetry_DoesNotMakeTheEntryPointStopBeingAPointOfPassage()
    {
        // QA V14 B1 (T1a -> T1b). Api -> Svc -> Db, and then the instrumentation
        // NO_OBSERVABILITY asks for on top of it. Obs became the root of the graph and reached Svc
        // and Db down its own scrape cables, so nothing was left unreachable without the Api and
        // its Critical disappeared - the analyzer dropping the worst finding on the board as a
        // reward for following another rule's advice.
        _builder
            .Node("Api", NodeType.Api)
            .Node("Svc", NodeType.Service)
            .Node("Db", NodeType.Database, """{"replicas": 2}""")
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Svc", timeoutSeconds: 5, fallback: true)
            .Connect("Svc", "Db", timeoutSeconds: 5, fallback: true)
            .Connect("Obs", "Api", synchronous: false)
            .Connect("Obs", "Svc", synchronous: false)
            .Connect("Obs", "Db", synchronous: false);

        var api = Assert.Single(
            _rule.Evaluate(_builder.Build()).Findings,
            f => f.Elements.Contains(_builder.NodeElement("Api")));

        Assert.Equal(Severity.Critical, api.Severity);
        Assert.Equal("finding.SPOF.unreachable.message(count=2, node=Api)", api.Message.Describe());

        // And the severity is the measurement's, not a constant (V13 D4.1): the Api is still the
        // front door of this architecture, so losing it still costs every request. The telemetry
        // arrow used to take it out of the graph's roots and with it out of that answer too.
        Assert.Equal("finding.SPOF.explanation.measured(affected=1, node=Api)", api.Explanation.Describe());
    }

    [Fact]
    public void InstrumentingAGraph_ChangesNothingAboutItsSpofs()
    {
        // The general form of the same promise, and the one that outlives the next rule change:
        // two graphs that differ only by telemetry have to come out of this rule identical - same
        // components, same severities, same counts, same sentences. The explanation is in the
        // comparison on purpose: it is where the second half of the bug showed, with the Db's
        // measured "100 % of requests" turning into "every caller is covered" because the walk that
        // measures it had started at the metrics sink.
        var bare = Instrumented(new GraphBuilder(), telemetry: false);
        var observed = Instrumented(new GraphBuilder(), telemetry: true);

        Assert.Equal(
            Described(bare, _rule.Evaluate(bare.Build()).Findings),
            Described(observed, _rule.Evaluate(observed.Build()).Findings));

        static GraphBuilder Instrumented(GraphBuilder builder, bool telemetry)
        {
            builder
                .Node("Gateway", NodeType.Gateway)
                .Nodes(NodeType.Service, "Users", "Orders")
                .Node("Db", NodeType.Database)
                .Connect("Gateway", "Users").Connect("Gateway", "Orders")
                .Connect("Users", "Db").Connect("Orders", "Db");

            if (telemetry)
                builder
                    .Node("Obs", NodeType.Observability)
                    .Connect("Obs", "Gateway", synchronous: false)
                    .Connect("Obs", "Users", synchronous: false)
                    .Connect("Obs", "Orders", synchronous: false)
                    .Connect("Obs", "Db", synchronous: false);

            return builder;
        }

        static List<string> Described(GraphBuilder builder, IReadOnlyList<RuleFinding> findings) =>
            findings
                .Select(f => $"{string.Join(",", builder.Names(f.Elements))} {f.Severity} {f.Message.Describe()} {f.Explanation.Describe()}")
                .OrderBy(line => line, StringComparer.Ordinal)
                .ToList();
    }

    // ---- V14 B11: redundancy is drawn or declared, never inferred --------------

    [Fact]
    public void TwoBusinessServices_SharingTheirCallers_AreTwoSinglePointsOfFailure()
    {
        // T5 in the sweep: `Gw -> Users`, `Gw -> Orders`, `Api -> Users`, `Api -> Orders`. Two
        // business services, each with two hard dependents, each a Warning - and SPOF showed a
        // green tick because they exempted each other for having the same callers.
        _builder
            .Node("Gw", NodeType.Gateway).Node("Api", NodeType.Api)
            .Nodes(NodeType.Service, "Users", "Orders")
            .Connect("Gw", "Users").Connect("Gw", "Orders")
            .Connect("Api", "Users").Connect("Api", "Orders");

        var findings = _rule.Evaluate(_builder.Build()).Findings;

        Assert.Contains(findings, f => f.Elements.Contains(_builder.NodeElement("Users")) && f.Severity == Severity.Warning);
        Assert.Contains(findings, f => f.Elements.Contains(_builder.NodeElement("Orders")) && f.Severity == Severity.Warning);
    }

    [Fact]
    public void TheTwoWaysToSayCopies_StillClearIt()
    {
        // What replaces the guess: the drawn way and the declared way. Same T5 wiring, and
        // either one turns both Warnings off.
        var drawn = new GraphBuilder()
            .Node("LB", NodeType.LoadBalancer).Node("Gw", NodeType.Gateway)
            .Nodes(NodeType.Service, "Users1", "Users2")
            .Connect("LB", "Users1").Connect("LB", "Users2")
            .Connect("Gw", "Users1").Connect("Gw", "Users2");

        Assert.Empty(_rule.Evaluate(drawn.Build()).Findings);

        var declared = new GraphBuilder()
            .Node("Gw", NodeType.Gateway).Node("Api", NodeType.Api)
            .Node("Users", NodeType.Service, """{"instances": 3}""")
            .Connect("Gw", "Users").Connect("Api", "Users");

        Assert.Empty(_rule.Evaluate(declared.Build()).Findings);
    }

    [Fact]
    public void ABalancerOverTwoDifferentApis_IsAKnownLimit()
    {
        // T14, pinned as the limit it is and not as a bug to fix (V14 B11). A balancer spreading
        // over ApiPublica and ApiAdmin exempts both, because from the wiring alone two boxes of
        // one type behind one balancer is exactly what two copies look like - it is the drawing
        // convention for replicas, and the rule cannot read names. If this ever flips, it is
        // because somebody gave the product a way to draw "different" that is not "a name".
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Api, "ApiPublica", "ApiAdmin")
            .Node("Db", NodeType.Database, """{"replicas": 1}""")
            .Connect("LB", "ApiPublica").Connect("LB", "ApiAdmin")
            .Connect("ApiPublica", "Db").Connect("ApiAdmin", "Db");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    // ---- V14 B12: the identity provider is a SPOF with one dependent -----------

    [Fact]
    public void TheIdentityProviderOnTheDoorsPath_IsTheFamousSpof()
    {
        // T12, the canonical shape: `Client -> Gw`, `Gw -> Auth0` with a timeout and no fallback,
        // `Gw -> Db`. Pattern A wanted two dependents and the IdP had one; pattern B saw nothing
        // behind it; the analyzer said nothing about Auth0 and scored Resilience 100. If Auth0 is
        // down nobody authenticates, which is every request the door admits - and the severity is
        // that measurement, not the count of one.
        _builder
            .Node("Web", NodeType.Client).Node("Gw", NodeType.Gateway)
            .Node("Auth0", NodeType.IdentityProvider).Node("Db", NodeType.Database, """{"replicas": 1}""")
            .Connect("Web", "Gw")
            .Connect("Gw", "Auth0", timeoutSeconds: 2)
            .Connect("Gw", "Db", timeoutSeconds: 5, fallback: true, protocol: ProtocolType.Sql);

        var auth = Assert.Single(
            _rule.Evaluate(_builder.Build()).Findings,
            f => f.Elements.Contains(_builder.NodeElement("Auth0")));

        Assert.Equal(Severity.Critical, auth.Severity);
        Assert.Equal("finding.SPOF.shared.message(count=1, node=Auth0)", auth.Message.Describe());
        Assert.Equal("finding.SPOF.explanation.measured(affected=1, node=Auth0)", auth.Explanation.Describe());
        Assert.Equal("finding.SPOF.suggestion.identityProvider", auth.Suggestion.Key());
    }

    [Fact]
    public void AFallbackOnTheCallToTheIdentityProvider_IsTheWayOut()
    {
        // The loop the suggestion closes: "decide what a valid session does when the provider is
        // away" is the fallback on the edge, and with it the door no longer falls with Auth0.
        _builder
            .Node("Web", NodeType.Client).Node("Gw", NodeType.Gateway)
            .Node("Auth0", NodeType.IdentityProvider)
            .Connect("Web", "Gw")
            .Connect("Gw", "Auth0", timeoutSeconds: 2, fallback: true);

        Assert.DoesNotContain(_rule.Evaluate(_builder.Build()).Findings, f => f.Elements.Contains(_builder.NodeElement("Auth0")));
    }

    [Fact]
    public void AnIdentityProviderBehindACoveredCaller_IsAWarningThatSaysSo()
    {
        // The same reading pattern B has had since QA B1: a covered caller is a Warning whose card
        // says no request is lost. Svc blocks on the IdP, but the door reaches Svc with a fallback,
        // so losing Auth0 fails no request coming in the front.
        _builder
            .Node("Web", NodeType.Client).Node("Gw", NodeType.Gateway)
            .Node("Svc", NodeType.Service).Node("Auth0", NodeType.IdentityProvider)
            .Connect("Web", "Gw")
            .Connect("Gw", "Svc", timeoutSeconds: 5, fallback: true)
            .Connect("Svc", "Auth0", timeoutSeconds: 2);

        var auth = Assert.Single(
            _rule.Evaluate(_builder.Build()).Findings,
            f => f.Elements.Contains(_builder.NodeElement("Auth0")));

        Assert.Equal(Severity.Warning, auth.Severity);
        Assert.Equal("finding.SPOF.explanation.covered", auth.Explanation.Key());
    }

    [Fact]
    public void OneDependentIsStillNotEnough_ForAnythingElse()
    {
        // The exception is the identity provider's and nobody else's: a Service with one hard
        // dependent is a dependency, not a single point of failure.
        _builder
            .Node("Gw", NodeType.Gateway).Node("Svc", NodeType.Service)
            .Connect("Gw", "Svc", timeoutSeconds: 2);

        Assert.DoesNotContain(_rule.Evaluate(_builder.Build()).Findings, f => f.Elements.Contains(_builder.NodeElement("Svc")));
    }

    // ---- V14 B9: nobody depends on their metrics sink ------------------------

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InstrumentingAGraph_ChangesNothing_HoweverTheWireIsDrawn(bool push, bool synchronous)
    {
        // The promise above, over the four shapes a student can actually draw instead of the one
        // the helper drew. Both halves matter: the canvas defaults a new edge to *synchronous*
        // (DEFAULT_CONNECTION_DATA), and the guided fix of NO_OBSERVABILITY draws the *push*
        // direction - so `Api -> Obs` synchronous is what you get by following the advice and then
        // touching the wire. It used to make the telemetry box a Critical SPOF that four
        // components depended on, and a scrape drawn the same way turned the database's Warning
        // into a Critical by being its third dependent.
        var bare = Wired(new GraphBuilder(), telemetry: false, push, synchronous);
        var observed = Wired(new GraphBuilder(), telemetry: true, push, synchronous);

        Assert.Equal(
            Describe(bare, _rule.Evaluate(bare.Build()).Findings),
            Describe(observed, _rule.Evaluate(observed.Build()).Findings));

        static GraphBuilder Wired(GraphBuilder builder, bool telemetry, bool push, bool synchronous)
        {
            builder
                .Node("Gateway", NodeType.Gateway)
                .Nodes(NodeType.Service, "Users", "Orders")
                .Node("Db", NodeType.Database)
                .Connect("Gateway", "Users").Connect("Gateway", "Orders")
                .Connect("Users", "Db").Connect("Orders", "Db");

            if (!telemetry)
                return builder;

            builder.Node("Obs", NodeType.Observability);

            foreach (var name in new[] { "Gateway", "Users", "Orders", "Db" })
            {
                if (push)
                    builder.Connect(name, "Obs", synchronous);
                else
                    builder.Connect("Obs", name, synchronous);
            }

            return builder;
        }

        static List<string> Describe(GraphBuilder builder, IReadOnlyList<RuleFinding> findings) =>
            findings
                .Select(f => $"{string.Join(",", builder.Names(f.Elements))} {f.Severity} {f.Message.Describe()} {f.Explanation.Describe()}")
                .OrderBy(line => line, StringComparer.Ordinal)
                .ToList();
    }

    [Fact]
    public void TheTelemetryBox_IsNeverASinglePointOfFailure()
    {
        // The named repro of what pattern A did on its own: four components pushing their metrics
        // synchronously, and the sink comes out the worst finding on the board. Losing it loses
        // the graphs, not the request - which is the reason CapacityModel gives it an enormous
        // capacity and the walk refuses to enter through it.
        _builder
            .Nodes(NodeType.Service, "A", "B", "C")
            .Node("Obs", NodeType.Observability)
            .Connect("A", "Obs").Connect("B", "Obs").Connect("C", "Obs");

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }

    [Fact]
    public void TheScrape_DoesNotEscalateWhatItWatches()
    {
        // The quieter half, and the one nobody would have looked for: with the collector counted
        // as a third dependent, wiring the telemetry pushed the database from Warning to Critical.
        _builder
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Service, "Users", "Orders")
            .Node("Obs", NodeType.Observability)
            .Connect("Users", "Db").Connect("Orders", "Db")
            .Connect("Obs", "Db");

        var db = Assert.Single(
            _rule.Evaluate(_builder.Build()).Findings,
            f => f.Elements.Contains(_builder.NodeElement("Db")));

        Assert.Equal(Severity.Warning, db.Severity);
        Assert.Equal("finding.SPOF.shared.message(count=2, node=Db)", db.Message.Describe());
    }

    [Fact]
    public void TheTelemetryBoxItselfIsNeverAPointOfPassage()
    {
        // The other half of the same doctrine - the half that used to be spelled out as an extra
        // condition on the counting side while the walk contradicted it. Obs points at three
        // components and is a point of passage for none of them: nothing was ever flowing down
        // those arrows.
        _builder
            .Node("Obs", NodeType.Observability)
            .Node("Api", NodeType.Api)
            .Nodes(NodeType.Service, "Users", "Orders")
            .Connect("Api", "Users").Connect("Api", "Orders")
            .Connect("Obs", "Api", synchronous: false)
            .Connect("Obs", "Users", synchronous: false)
            .Connect("Obs", "Orders", synchronous: false);

        var findings = _rule.Evaluate(_builder.Build()).Findings;

        Assert.DoesNotContain(findings, f => f.Elements.Contains(_builder.NodeElement("Obs")));
        Assert.Single(findings, f => f.Elements.Contains(_builder.NodeElement("Api")));
    }

    // ---- V13 D1.2: the advice is the component's, not the code's --------------

    [Theory]
    [InlineData(NodeType.Database, "finding.SPOF.suggestion.database")]
    [InlineData(NodeType.Cache, "finding.SPOF.suggestion.cache")]
    [InlineData(NodeType.Queue, "finding.SPOF.suggestion.queue")]
    [InlineData(NodeType.ObjectStorage, "finding.SPOF.suggestion.objectStorage")]
    [InlineData(NodeType.ExternalService, "finding.SPOF.suggestion.external")]
    [InlineData(NodeType.IdentityProvider, "finding.SPOF.suggestion.identityProvider")]
    [InlineData(NodeType.Gateway, "finding.SPOF.suggestion.entryPoint")]
    [InlineData(NodeType.LoadBalancer, "finding.SPOF.suggestion.entryPoint")]
    [InlineData(NodeType.Service, "finding.SPOF.suggestion.instances")]
    [InlineData(NodeType.Worker, "finding.SPOF.suggestion.instances")]
    [InlineData(NodeType.Api, "finding.SPOF.suggestion.instances")]
    public void TheSuggestionIsPickedByType(NodeType type, string expected)
    {
        // "Add a balancer with two equivalent instances, or replicate it" was being said to a
        // Postgres and to somebody else's payment provider, where it is not even available advice.
        _builder
            .Node("Target", type)
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Target").Connect("Payments", "Target");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(expected, finding.Suggestion.Key());
    }

    // ---- V13 D4.1: how much, not how many -------------------------------------

    [Fact]
    public void TheExplanationCarriesTheMeasuredImpact()
    {
        // The sentence the review asked for: "if Db falls you lose 100 % of requests" instead of
        // "3 components depend on it". Both callers block on it with no fallback, so everything
        // entering the front door stops being served.
        _builder
            .Node("Gateway", NodeType.Gateway)
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Node("Db", NodeType.Database)
            .Connect("Gateway", "Orders", timeoutSeconds: 5)
            .Connect("Gateway", "Payments", timeoutSeconds: 5)
            .Connect("Orders", "Db", timeoutSeconds: 5)
            .Connect("Payments", "Db", timeoutSeconds: 5);

        var db = Assert.Single(_rule.Evaluate(_builder.Build()).Findings,
            f => f.Elements.Contains(_builder.NodeElement("Db")));

        Assert.Equal("finding.SPOF.explanation.measured(affected=1, node=Db)", db.Explanation.Describe());
    }

    [Fact]
    public void AFrontDoorCostsEverything()
    {
        // **The case that made this a helper instead of one call.** `RequestYield` walks *from* the
        // roots and only ever asks whether the target of an edge is down, so asked plainly it said a
        // dead gateway costs 0 % of requests - on the flagship SPOF of the whole product.
        _builder
            .Node("Gateway", NodeType.Gateway)
            .Nodes(NodeType.Service, "Users", "Orders", "Payments")
            .Connect("Gateway", "Users", timeoutSeconds: 5)
            .Connect("Gateway", "Orders", timeoutSeconds: 5)
            .Connect("Gateway", "Payments", timeoutSeconds: 5);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.SPOF.explanation.measured", finding.Explanation.Key());
        Assert.Equal("1", finding.Explanation.Arg("affected"));
    }

    [Fact]
    public void WhenEveryCallerIsCoveredTheSentenceChanges()
    {
        // `resilient-async`'s database, reduced. It is a single point of failure - a fallback cannot
        // give a write back (D1.1) - and not one request stops being served. Printing "0 % of
        // requests" next to a Warning would read as the analyzer arguing with itself, so this case
        // gets its own sentence and no number.
        _builder
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Db", timeoutSeconds: 5, circuitBreaker: true, fallback: true)
            .Connect("Payments", "Db", timeoutSeconds: 5, circuitBreaker: true, fallback: true);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.SPOF.explanation.covered(node=Db)", finding.Explanation.Describe());
        Assert.Null(finding.Explanation.Arg("affected"));
    }

    [Fact]
    public void OnlyTheUnprotectedEntryPointLosesItsRequests()
    {
        // Partial protection, which is where the figure stops being 0 or 1 and starts being worth
        // printing. Two front doors share the database; one falls back and the other does not, and
        // the bound is the worst of them (V4 DT.7) - so it is 100 % of the door that blocks, not an
        // average over the two.
        _builder
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Api, "Public", "Admin")
            .Connect("Public", "Db", timeoutSeconds: 5, fallback: true)
            .Connect("Admin", "Db", timeoutSeconds: 5);

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal("finding.SPOF.explanation.measured", finding.Explanation.Key());
        Assert.Equal("1", finding.Explanation.Arg("affected"));
    }

    [Fact]
    public void AsynchronousConsumersDoNotLoseRequestsEither()
    {
        // A queue nobody blocks on. It earns a finding through pattern B - two components go
        // unreachable behind it - and it costs no request at all, which are two true things at once
        // and exactly the pair the old constant could not say.
        _builder
            .Node("Api", NodeType.Api)
            .Node("Queue", NodeType.Queue)
            .Nodes(NodeType.Worker, "Mailer", "Indexer")
            .Connect("Api", "Queue", synchronous: false)
            .Connect("Queue", "Mailer", synchronous: false)
            .Connect("Queue", "Indexer", synchronous: false);

        var findings = _rule.Evaluate(_builder.Build()).Findings;

        // The Api is flagged too, and rightly - it is the front door, and D4.1 says a dead front
        // door costs everything. The two findings on the same graph are the decision in miniature.
        var queue = Assert.Single(findings, f => f.Elements.Contains(_builder.NodeElement("Queue")));
        Assert.Equal("finding.SPOF.explanation.covered(node=Queue)", queue.Explanation.Describe());

        var api = Assert.Single(findings, f => f.Elements.Contains(_builder.NodeElement("Api")));
        Assert.Equal("finding.SPOF.explanation.measured(affected=1, node=Api)", api.Explanation.Describe());
    }

    [Fact]
    public void Evaluate_PublishesOneFactPerFindingCarryingItsSeverity()
    {
        // MISSING_RESILIENCE escalates only towards a Critical SPOF, so the fact and the finding
        // must agree on the severity (D5.1).
        _builder
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Db").Connect("Payments", "Db");

        var graph = _builder.Build();
        var findings = _rule.Evaluate(graph).Findings;

        var fact = Assert.IsType<SinglePointOfFailureFact>(Assert.Single(graph.Facts));
        Assert.Equal(_builder.IdOf("Db"), fact.NodeId);
        Assert.Equal(Assert.Single(findings).Severity, fact.Severity);
    }

    [Fact]
    public void Evaluate_NoNodeHasDependents_IsNotApplicable()
    {
        _builder.Node("Api", NodeType.Api).Node("Db", NodeType.Database);

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }
}
