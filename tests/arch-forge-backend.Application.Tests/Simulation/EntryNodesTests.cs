using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Simulation;
using bondstone_backend.Application.Tests.Challenges;
using bondstone_backend.Domain.Enums;
using Xunit;

namespace bondstone_backend.Application.Tests.Simulation;

public class EntryNodesTests
{
    // A root is a node nobody calls; an entry point is a root that calls someone. The loose box is a
    // root by the letter and, until 30-08, one of the «points of entry» the metrics counted: losing it
    // read as a customer-facing failure, and deleting a call read as containing one (QA §3.6).
    [Fact]
    public void A_box_with_no_calls_is_a_root_and_not_where_requests_enter()
    {
        var graph = ChallengeHarness.Graph(Blueprint.Of(
            [("Api", NodeType.Api, """{"requestsPerSecond":100}"""), ("Db", NodeType.Database, "{}"), ("Loose", NodeType.Service, "{}")],
            [("Api", "Db", true, """{"timeoutSeconds":5}""")]));

        Assert.Equal(2, graph.RootNodes.Count);
        Assert.Equal(["Api"], graph.EntryNodes.Select(n => n.Name));

        var loose = graph.Nodes.Single(n => n.Name == "Loose");
        var metrics = new SimulationEngine()
            .Run(graph, new SimulationScenario([new Fault(ElementType.Node, loose.Id, FaultKinds.Down)]))
            .Metrics;

        Assert.Equal(1, metrics.EntryPointsTotal);
        Assert.Equal(0, metrics.EntryPointsAffected);
        Assert.Equal(0m, metrics.RequestsAffected);
        Assert.Equal(1, metrics.ComponentsDown);
    }

    // QA 30-08 §4.1 / 7.3.3: with only the telemetry down, every component that pushed to it came out
    // «degraded, contained» — the root included — and the panel said the customer noticed. Nobody was
    // serving worse: the collector was down. The rules already skipped telemetry edges; the
    // propagation now does too, and an observability box is never where requests enter.
    [Fact]
    public void Losing_the_telemetry_is_not_the_customer_noticing_anything()
    {
        var graph = ChallengeHarness.Graph(Blueprint.Of(
            [("Api", NodeType.Api, """{"requestsPerSecond":100}"""), ("Db", NodeType.Database, "{}"), ("Obs", NodeType.Observability, "{}")],
            [("Api", "Db", true, """{"timeoutSeconds":5}"""),
             ("Api", "Obs", false, "{}"),
             ("Db", "Obs", false, "{}"),
             ("Obs", "Api", true, """{"timeoutSeconds":5}""")]));

        Assert.Equal(["Api"], graph.EntryNodes.Select(n => n.Name));

        var obs = graph.Nodes.Single(n => n.Name == "Obs");
        var result = new SimulationEngine()
            .Run(graph, new SimulationScenario([new Fault(ElementType.Node, obs.Id, FaultKinds.Down)]));

        Assert.Equal(HealthState.Healthy, result.States.Single(s => s.Id == graph.Nodes.Single(n => n.Name == "Api").Id).State);
        Assert.Equal(0, result.Metrics.EntryPointsAffected);
        Assert.Equal(1, result.Metrics.EntryPointsTotal);
        Assert.Equal(1, result.Metrics.ComponentsDown);
        Assert.Equal(0, result.Metrics.ComponentsDegraded);
    }
}
