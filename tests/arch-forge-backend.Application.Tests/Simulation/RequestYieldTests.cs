using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using arch_forge_backend.Application.Tests.Analysis.Rules.Support;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation;

public class RequestYieldTests
{
    [Fact]
    public void Two_bottlenecks_in_series_give_the_worst_of_the_two_and_not_their_product()
    {
        var graph = Loaded(600, builder => builder
            .Node("A", NodeType.Service)
            .Node("B", NodeType.Service)
            .Connect("Api", "A")
            .Connect("A", "B"));

        var (affected, bottleneck) = Impact(graph);

        Assert.Equal(0.5m, affected);
        Assert.Equal(FindingElement.Node(graph.NodeId("A")), bottleneck);
    }

    [Fact]
    public void A_timeout_and_a_fallback_contain_whatever_is_behind_them()
    {
        var graph = Loaded(300, builder => builder
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 5, fallback: true));

        var (affected, bottleneck) = Impact(graph);

        Assert.Equal(0m, affected);
        Assert.Null(bottleneck);
    }

    [Fact]
    public void A_fallback_without_a_timeout_contains_nothing()
    {
        var graph = Loaded(300, builder => builder
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", fallback: true));

        var (affected, _) = Impact(graph);

        Assert.Equal(0.3333m, affected);
    }

    [Fact]
    public void An_asynchronous_call_never_holds_a_request_back()
    {
        var graph = Loaded(300, builder => builder
            .Node("Queue", NodeType.Queue)
            .Node("Worker", NodeType.Service)
            .Connect("Api", "Queue", synchronous: false)
            .Connect("Queue", "Worker", synchronous: false));

        var (affected, _) = Impact(graph);

        Assert.Equal(0m, affected);
    }

    [Fact]
    public void A_dead_dependency_with_no_alternative_serves_nothing()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 5)
            .Build();

        var down = new HashSet<Guid> { graph.NodeId("Db") };
        var yield = RequestYield.Of(graph, LoadModel.Of(graph), new YieldInputs(down, new HashSet<Guid>()));

        var (affected, bottleneck) = yield.Impact();

        Assert.Equal(1m, affected);
        Assert.Equal(FindingElement.Node(graph.NodeId("Db")), bottleneck);
    }

    [Fact]
    public void A_call_that_expires_every_time_is_the_same_as_a_dead_target()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db", timeoutSeconds: 5)
            .Build();

        var expired = new HashSet<Guid> { graph.EdgeId("Api", "Db") };
        var yield = RequestYield.Of(graph, LoadModel.Of(graph), new YieldInputs(new HashSet<Guid>(), expired));

        Assert.Equal(1m, yield.Impact().Affected);
    }

    [Fact]
    public void A_cycle_does_not_hang_the_walk()
    {
        var graph = Loaded(100, builder => builder
            .Node("A", NodeType.Service)
            .Node("B", NodeType.Service)
            .Connect("Api", "A")
            .Connect("A", "B")
            .Connect("B", "A"));

        Assert.Equal(0m, Impact(graph).Affected);
    }

    [Fact]
    public void With_nothing_declared_and_nothing_broken_nothing_is_affected()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Db")
            .Build();

        Assert.Equal(0m, Impact(graph).Affected);
    }

    // QA 30-08 §10.3, point 7 (P19). The door's only call did not wait, so nothing behind it could
    // ever reduce what it served: `Core` down gave 0 % of requests affected while the entry point
    // itself was counted as affected. A door that answers nobody serves nothing, fault or no fault.
    [Fact]
    public void A_door_that_waits_for_nobody_serves_nobody()
    {
        var graph = Loaded(100, builder => builder
            .Node("Core", NodeType.Service)
            .Connect("Api", "Core", synchronous: false));

        var (affected, bottleneck) = Impact(graph);

        Assert.Equal(1m, affected);
        Assert.Equal(FindingElement.Node(graph.NodeId("Api")), bottleneck);
    }

    [Fact]
    public void A_door_that_queues_the_work_serves_the_request_it_accepted()
    {
        var graph = Loaded(100, builder => builder
            .Node("Jobs", NodeType.Queue)
            .Node("Worker", NodeType.Worker)
            .Connect("Api", "Jobs", synchronous: false)
            .Connect("Jobs", "Worker", synchronous: false));

        Assert.Equal(0m, Impact(graph).Affected);
    }

    [Fact]
    public void One_call_that_waits_is_enough_for_the_door_to_be_answering()
    {
        var graph = Loaded(100, builder => builder
            .Node("Core", NodeType.Service)
            .Node("Mailer", NodeType.Service)
            .Connect("Api", "Core", timeoutSeconds: 3)
            .Connect("Api", "Mailer", synchronous: false));

        Assert.Equal(0m, Impact(graph).Affected);
    }

    // QA 30-08 §10.3, point 10. `Users → Api → Db` with a fallback and `Cron → Reports → Db` without
    // one: with the database down the users were served whole, and the panel still said 100 % of
    // requests lost and one entry point of two affected — the cron. What a clock sets off is work,
    // and the manual has always said a flow born in a Scheduler is not user traffic.
    [Fact]
    public void What_a_clock_sets_off_is_not_what_the_client_notices()
    {
        var graph = new GraphBuilder()
            .Node("Users", NodeType.Client, """{"requestsPerSecond":100}""")
            .Node("Api", NodeType.Api)
            .Node("Cron", NodeType.Scheduler, """{"jobsPerSecond":5}""")
            .Node("Reports", NodeType.Worker)
            .Node("Db", NodeType.Database)
            .Connect("Users", "Api")
            .Connect("Api", "Db", timeoutSeconds: 3, fallback: true)
            .Connect("Cron", "Reports")
            .Connect("Reports", "Db", timeoutSeconds: 3)
            .Build();

        Assert.Equal(["Users"], graph.UserEntryNodes.Select(n => n.Name));

        var down = new YieldInputs(new HashSet<Guid> { graph.NodeId("Db") }, new HashSet<Guid>());

        Assert.Equal(0m, RequestYield.Of(graph, LoadModel.Of(graph), down).Impact().Affected);
    }

    // QA 30-08 §10.3, point 11: the alternative answer arrives after the timeout expires, so one
    // behind a 60 s timeout reaches nobody — the same threshold TIMEOUT_TOO_LONG calls a wait and
    // not a limit, and the lesson `nobody-noticed` teaches.
    [Theory]
    [InlineData(5, 0)]
    [InlineData(29, 0)]
    [InlineData(30, 1)]
    [InlineData(60, 1)]
    public void A_fallback_that_arrives_too_late_serves_nobody(int timeoutSeconds, decimal affected)
    {
        var graph = Loaded(100, builder => builder
            .Node("Orders", NodeType.Service)
            .Connect("Api", "Orders", timeoutSeconds: timeoutSeconds, fallback: true));

        var down = new YieldInputs(new HashSet<Guid> { graph.NodeId("Orders") }, new HashSet<Guid>());

        Assert.Equal(affected, RequestYield.Of(graph, LoadModel.Of(graph), down).Impact().Affected);
    }

    private static (decimal Affected, FindingElement? Bottleneck) Impact(ArchitectureGraph graph) =>
        RequestYield.Of(graph, LoadModel.Of(graph), YieldInputs.CapacityOnly).Impact();

    private static ArchitectureGraph Loaded(int rps, Func<GraphBuilder, GraphBuilder> build) =>
        build(new GraphBuilder().Node("Api", NodeType.Api, $$"""{"requestsPerSecond":{{rps}}}""")).Build();
}
