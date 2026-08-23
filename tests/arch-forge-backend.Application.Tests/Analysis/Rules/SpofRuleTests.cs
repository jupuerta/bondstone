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
    public void EquivalentSibling_SameTypeAndSameDependents_IsExempt()
    {
        // Stateless siblings only (QA B2): two Services serving exactly the same consumers are
        // interchangeable copies. The same wiring over two Databases proves nothing - they would
        // be two truths, not two replicas - and no longer earns the exemption; see
        // QaRegressionTests.B2_TwoDifferentDatabasesWithTheSameConsumers_AreBothFlagged.
        _builder
            .Nodes(NodeType.Service, "Search1", "Search2")
            .Nodes(NodeType.Api, "Api1", "Api2")
            .Connect("Api1", "Search1").Connect("Api1", "Search2")
            .Connect("Api2", "Search1").Connect("Api2", "Search2");

        Assert.Empty(_rule.Evaluate(_builder.Build()).Findings);
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

    // ---- V13 D1.2: the advice is the component's, not the code's --------------

    [Theory]
    [InlineData(NodeType.Database, "finding.SPOF.suggestion.database")]
    [InlineData(NodeType.Cache, "finding.SPOF.suggestion.cache")]
    [InlineData(NodeType.Queue, "finding.SPOF.suggestion.queue")]
    [InlineData(NodeType.ObjectStorage, "finding.SPOF.suggestion.objectStorage")]
    [InlineData(NodeType.ExternalService, "finding.SPOF.suggestion.external")]
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
