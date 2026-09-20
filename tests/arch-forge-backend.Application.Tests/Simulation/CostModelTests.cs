using System.Reflection;
using bondstone_backend.Application.Analysis.Rules;
using bondstone_backend.Application.Simulation;
using bondstone_backend.Application.Tests.Analysis.Rules.Support;
using bondstone_backend.Domain.Enums;
using Xunit;

namespace bondstone_backend.Application.Tests.Simulation;

public class CostModelTests
{
    [Fact]
    public void Every_node_type_has_a_cost_somebody_chose()
    {
        foreach (var type in Enum.GetValues<NodeType>())
        {
            var chosen = ConstantOf(type.ToString());

            Assert.True(
                chosen is not null,
                $"{type} has no CostModel constant of its own, so PerInstance() answers for it "
                    + "through the default. That is V16 Anexo B: pick the number and name it.");

            Assert.Equal(chosen, CostModel.PerInstance(type));
        }
    }

    [Fact]
    public void The_unit_is_anchored_on_the_database()
    {
        Assert.Equal(10, CostModel.Database);
    }

    [Fact]
    public void A_topic_costs_what_a_queue_costs()
    {
        Assert.Equal(CostModel.Queue, CostModel.Topic);
    }

    [Fact]
    public void What_is_not_yours_is_free_and_everything_yours_has_a_price()
    {
        foreach (var type in Enum.GetValues<NodeType>())
            Assert.Equal(NodeRoles.IsYoursToPayFor(type), CostModel.PerInstance(type) > 0);
    }

    [Fact]
    public void Instances_multiply_cost_on_the_same_counter_capacity_reads()
    {
        var graph = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"instances":3}""")
            .Build();

        Assert.Equal(12, CostModel.Of(graph));
    }

    [Fact]
    public void Database_replicas_are_copies_you_pay_for()
    {
        var graph = new GraphBuilder()
            .Node("Db", NodeType.Database, """{"replicas":1,"readReplicas":1}""")
            .Build();

        Assert.Equal(30, CostModel.Of(graph));
    }

    [Fact]
    public void A_client_and_an_external_service_are_not_on_your_bill()
    {
        var graph = new GraphBuilder()
            .Node("Client", NodeType.Client)
            .Node("Api", NodeType.Api)
            .Node("Payments", NodeType.ExternalService)
            .Connect("Client", "Api")
            .Connect("Api", "Payments")
            .Build();

        Assert.Equal(CostModel.Api, CostModel.Of(graph));
    }

    [Fact]
    public void Spreading_across_zones_does_not_change_the_bill()
    {
        var stacked = new GraphBuilder()
            .Node("Api", NodeType.Api, """{"instances":3,"zone":"a"}""")
            .Build();

        var spread = new GraphBuilder()
            .Node("Api a", NodeType.Api, """{"zone":"a"}""")
            .Node("Api b", NodeType.Api, """{"zone":"b"}""")
            .Node("Api c", NodeType.Api, """{"zone":"c"}""")
            .Build();

        Assert.Equal(12, CostModel.Of(stacked));
        Assert.Equal(12, CostModel.Of(spread));
    }

    [Fact]
    public void Buying_iron_costs_24_and_the_cache_in_front_costs_16()
    {
        var iron = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Db", NodeType.Database, """{"replicas":1}""")
            .Connect("Api", "Db")
            .Build();

        var cache = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Cache", NodeType.Cache)
            .Node("Db", NodeType.Database)
            .Connect("Api", "Cache")
            .Connect("Cache", "Db")
            .Build();

        Assert.Equal(24, CostModel.Of(iron));
        Assert.Equal(16, CostModel.Of(cache));
    }

    private static int? ConstantOf(string name) =>
        typeof(CostModel)
            .GetField(name, BindingFlags.Public | BindingFlags.Static)
            ?.GetRawConstantValue() as int?;
}
