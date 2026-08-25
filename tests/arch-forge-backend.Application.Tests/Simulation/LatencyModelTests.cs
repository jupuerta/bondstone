using System.Reflection;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation;

public class LatencyModelTests
{
    [Fact]
    public void Every_node_type_has_a_processing_time_somebody_chose()
    {
        foreach (var type in Enum.GetValues<NodeType>())
        {
            var chosen = ConstantOf(type.ToString());

            Assert.True(
                chosen is not null,
                $"{type} has no LatencyModel constant of its own, so Processing() answers for it "
                    + "through the default. That is V16 Anexo B: pick the number and name it.");

            Assert.Equal(chosen, LatencyModel.Processing(type));
        }
    }

    [Fact]
    public void Every_protocol_has_a_hop_somebody_chose()
    {
        foreach (var protocol in Enum.GetValues<ProtocolType>())
        {
            var chosen = ConstantOf(protocol.ToString());

            Assert.True(chosen is not null, $"{protocol} has no LatencyModel constant of its own.");
            Assert.Equal(chosen, LatencyModel.Hop(protocol));
        }
    }

    [Fact]
    public void A_topic_costs_what_a_queue_costs()
    {
        Assert.Equal(LatencyModel.Queue, LatencyModel.Topic);
        Assert.Equal(CapacityModel.Queue, CapacityModel.Topic);
    }

    private static int? ConstantOf(string name) =>
        typeof(LatencyModel)
            .GetField(name, BindingFlags.Public | BindingFlags.Static)
            ?.GetRawConstantValue() as int?;
}
