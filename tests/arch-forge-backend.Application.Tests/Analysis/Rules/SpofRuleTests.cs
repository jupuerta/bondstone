using arch_forge_backend.Application.Tests.TestDoubles;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
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
        _builder
            .Node("Db", NodeType.Database)
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Db").Connect("Payments", "Db");

        Assert.Equal(Severity.Warning, Assert.Single(_rule.Evaluate(_builder.Build()).Findings).Severity);
    }

    [Fact]
    public void PatternA_SingleDependent_PassesInsteadOfFiring()
    {
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
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Connect("LB", "Api1").Connect("LB", "Api2");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void LoadBalancerWithASingleTarget_IsNotExempt()
    {
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


    [Fact]
    public void AStatelessComponent_IsClearedByDeclaredInstances()
    {
        var declared = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"instances": 2}""")
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Api").Connect("Payments", "Api");

        Assert.Empty(_rule.Evaluate(declared.Build()).Findings);

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
        _builder
            .Node("Api", NodeType.Api, configuration)
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Api").Connect("Payments", "Api");

        Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void DeclaringInstancesOnATypeThatHasNoSuchField_ClearsNothing()
    {
        _builder
            .Node("Queue", NodeType.Queue, """{"instances": 5}""")
            .Nodes(NodeType.Api, "Orders", "Payments")
            .Connect("Orders", "Queue").Connect("Payments", "Queue");

        Assert.Single(_rule.Evaluate(_builder.Build()).Findings);
    }

    [Fact]
    public void TheDeclaredFrontDoor_IsNoLongerAPointOfPassage()
    {
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
        _builder
            .Nodes(NodeType.Service, "Search", "Reporting")
            .Nodes(NodeType.Api, "Orders", "Payments", "Users")
            .Connect("Orders", "Search").Connect("Orders", "Reporting")
            .Connect("Payments", "Search").Connect("Users", "Search");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(_builder.NodeElement("Search"), Assert.Single(finding.Elements));
    }


    [Fact]
    public void AsynchronousDependents_DoNotCount()
    {
        _builder
            .Node("Queue", NodeType.Queue)
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Connect("Api1", "Queue", synchronous: false)
            .Connect("Api2", "Queue", synchronous: false);

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void DependentsHoldingAFallback_DoNotCount()
    {
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


    [Fact]
    public void WiringTheTelemetry_DoesNotMakeTheEntryPointStopBeingAPointOfPassage()
    {
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

        Assert.Equal("finding.SPOF.explanation.measured(affected=1, node=Api)", api.Explanation.Describe());
    }

    [Fact]
    public void InstrumentingAGraph_ChangesNothingAboutItsSpofs()
    {
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


    [Fact]
    public void TwoBusinessServices_SharingTheirCallers_AreTwoSinglePointsOfFailure()
    {
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
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Api, "ApiPublica", "ApiAdmin")
            .Node("Db", NodeType.Database, """{"replicas": 1}""")
            .Connect("LB", "ApiPublica").Connect("LB", "ApiAdmin")
            .Connect("ApiPublica", "Db").Connect("ApiAdmin", "Db");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
    }


    [Fact]
    public void TheIdentityProviderOnTheDoorsPath_IsTheFamousSpof()
    {
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
        _builder
            .Node("Gw", NodeType.Gateway).Node("Svc", NodeType.Service)
            .Connect("Gw", "Svc", timeoutSeconds: 2);

        Assert.DoesNotContain(_rule.Evaluate(_builder.Build()).Findings, f => f.Elements.Contains(_builder.NodeElement("Svc")));
    }


    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InstrumentingAGraph_ChangesNothing_HoweverTheWireIsDrawn(bool push, bool synchronous)
    {
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
        _builder
            .Nodes(NodeType.Service, "A", "B", "C")
            .Node("Obs", NodeType.Observability)
            .Connect("A", "Obs").Connect("B", "Obs").Connect("C", "Obs");

        Assert.False(_rule.Evaluate(_builder.Build()).IsApplicable);
    }

    [Fact]
    public void TheScrape_DoesNotEscalateWhatItWatches()
    {
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
        _builder
            .Node("Target", type)
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Orders", "Target").Connect("Payments", "Target");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(expected, finding.Suggestion.Key());
    }


    [Fact]
    public void TheExplanationCarriesTheMeasuredImpact()
    {
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
        _builder
            .Node("Api", NodeType.Api)
            .Node("Queue", NodeType.Queue)
            .Nodes(NodeType.Worker, "Mailer", "Indexer")
            .Connect("Api", "Queue", synchronous: false)
            .Connect("Queue", "Mailer", synchronous: false)
            .Connect("Queue", "Indexer", synchronous: false);

        var findings = _rule.Evaluate(_builder.Build()).Findings;

        var queue = Assert.Single(findings, f => f.Elements.Contains(_builder.NodeElement("Queue")));
        Assert.Equal("finding.SPOF.explanation.covered(node=Queue)", queue.Explanation.Describe());

        var api = Assert.Single(findings, f => f.Elements.Contains(_builder.NodeElement("Api")));
        Assert.Equal("finding.SPOF.explanation.measured(affected=1, node=Api)", api.Explanation.Describe());
    }

    [Fact]
    public void Evaluate_PublishesOneFactPerFindingCarryingItsSeverity()
    {
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


    [Fact]
    public void ABalancerOverOneTargetThatDeclaresInstances_IsTheRedundancyMechanismToo()
    {
        var declared = new GraphBuilder()
            .Node("Users", NodeType.Client).Node("LB", NodeType.LoadBalancer)
            .Node("Api", NodeType.Api, """{"instances": 2}""")
            .Node("Db", NodeType.Database, """{"replicas": 2}""")
            .Connect("Users", "LB").Connect("LB", "Api")
            .Connect("Api", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var drawn = new GraphBuilder()
            .Node("Users", NodeType.Client).Node("LB", NodeType.LoadBalancer)
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Node("Db", NodeType.Database, """{"replicas": 2}""")
            .Connect("Users", "LB").Connect("LB", "Api1").Connect("LB", "Api2")
            .Connect("Api1", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql)
            .Connect("Api2", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        Assert.Empty(_rule.Evaluate(drawn.Build()).Findings);
        Assert.Empty(_rule.Evaluate(declared.Build()).Findings);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"instances": 1}""")]
    [InlineData("""{"instances": 0}""")]
    public void ABalancerOverOneTargetThatDeclaresNothing_IsStillTheSinglePointOfFailure(string configuration)
    {
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Node("Api", NodeType.Api, configuration)
            .Node("Db", NodeType.Database)
            .Connect("LB", "Api").Connect("Api", "Db");

        var finding = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(_builder.NodeElement("LB"), Assert.Single(finding.Elements));
    }

    [Fact]
    public void TheDeclarationBehindTheBalancer_HasToBeOnATypeThatOwnsTheField()
    {
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Node("Gw", NodeType.Gateway, """{"instances": 5}""")
            .Nodes(NodeType.Service, "Users", "Orders")
            .Connect("LB", "Gw").Connect("Gw", "Users").Connect("Gw", "Orders");

        Assert.Contains(_rule.Evaluate(_builder.Build()).Findings, f => f.Elements.Contains(_builder.NodeElement("LB")));
    }

    [Fact]
    public void WhatTheBalancerSpreadsOver_IsTheOnlyThingItVouchesFor()
    {
        _builder
            .Node("LB", NodeType.LoadBalancer)
            .Node("Api", NodeType.Api, """{"instances": 2}""")
            .Node("Svc", NodeType.Service)
            .Nodes(NodeType.Service, "Users", "Orders")
            .Connect("LB", "Api").Connect("LB", "Svc")
            .Connect("Svc", "Users").Connect("Svc", "Orders");

        var findings = _rule.Evaluate(_builder.Build()).Findings;

        Assert.DoesNotContain(findings, f => f.Elements.Contains(_builder.NodeElement("LB")));
        Assert.Contains(findings, f => f.Elements.Contains(_builder.NodeElement("Svc")));
    }


    [Fact]
    public void TheCdn_IsNotGradedAsASinglePointOfFailure()
    {
        _builder
            .Node("Users", NodeType.Client).Node("Edge", NodeType.Cdn)
            .Node("Api", NodeType.Api, """{"instances": 3}""")
            .Node("Db", NodeType.Database, """{"replicas": 2}""")
            .Connect("Users", "Edge").Connect("Edge", "Api", timeoutSeconds: 5)
            .Connect("Api", "Db", timeoutSeconds: 5, protocol: ProtocolType.Sql);

        var result = _rule.Evaluate(_builder.Build());

        Assert.True(result.IsApplicable);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void AddingTheCdnThatMissingCdnAsksFor_CostsNoAvailability()
    {
        var bare = Fronted(new GraphBuilder(), cdn: false);
        var fronted = Fronted(new GraphBuilder(), cdn: true);

        Assert.Empty(_rule.Evaluate(bare.Build()).Findings);
        Assert.Empty(_rule.Evaluate(fronted.Build()).Findings);

        var without = ReferenceArchitecture.Analyze(bare.Build());
        var with = ReferenceArchitecture.Analyze(fronted.Build());

        Assert.Equal(without.ScoreOf(Area.Availability), with.ScoreOf(Area.Availability));
        Assert.DoesNotContain(with.Findings, f => f.Code == RuleCodes.Spof && f.Severity != Severity.Good);
        Assert.True(with.Score > without.Score, $"with the CDN {with.Score}, without it {without.Score}");

        static GraphBuilder Fronted(GraphBuilder builder, bool cdn)
        {
            builder
                .Node("Users", NodeType.Client, """{"requestsPerSecond": 3000}""")
                .Node("Api", NodeType.Api, """{"authentication": true, "rateLimit": 9000, "instances": 20}""")
                .Node("Db", NodeType.Database, """{"replicas": 2, "readReplicas": 40}""")
                .Connect("Api", "Db", timeoutSeconds: 30, fallback: true, protocol: ProtocolType.Sql);

            if (cdn)
                builder.Node("Edge", NodeType.Cdn).Connect("Users", "Edge").Connect("Edge", "Api", timeoutSeconds: 30, fallback: true);
            else
                builder.Connect("Users", "Api");

            return builder;
        }
    }

    [Fact]
    public void WhatTheCdnFronts_IsStillGraded()
    {
        _builder
            .Node("Users", NodeType.Client).Node("Edge", NodeType.Cdn).Node("Gw", NodeType.Gateway)
            .Nodes(NodeType.Service, "Orders", "Payments")
            .Connect("Users", "Edge").Connect("Edge", "Gw", timeoutSeconds: 5)
            .Connect("Gw", "Orders", timeoutSeconds: 5).Connect("Gw", "Payments", timeoutSeconds: 5);

        var gw = Assert.Single(_rule.Evaluate(_builder.Build()).Findings);

        Assert.Equal(_builder.NodeElement("Gw"), Assert.Single(gw.Elements));
        Assert.Equal(Severity.Critical, gw.Severity);
        Assert.Equal("finding.SPOF.unreachable.message(count=2, node=Gw)", gw.Message.Describe());
        Assert.Equal("finding.SPOF.explanation.measured(affected=1, node=Gw)", gw.Explanation.Describe());
    }
}
