using System.Reflection;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation;

/// <summary>
/// V16 Anexo B. The bug this file exists for was not a wrong number: it was a number nobody chose.
///
/// <see cref="LatencyModel.Processing"/> listed fifteen of the sixteen node types, so a
/// <see cref="NodeType.Topic"/> fell through <c>_ =&gt; Service</c> and cost 10 ms — five times its
/// twin the <see cref="NodeType.Queue"/>, which <see cref="CapacityModel"/> says in as many words is
/// the same broker hardware. Nothing was broken and nothing failed; the switch simply had a default
/// and the enum grew past it, and a default is exactly the shape a decision does not have.
///
/// So what is asserted here is <strong>totality</strong>, not values. Every type must own a named
/// constant and the switch must return it, which means the seventeenth type cannot be given a
/// latency by accident: it either gets a figure somebody wrote down, or this fails.
/// </summary>
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
        // The same default sits under Hop(). Three protocols today, and the fourth is when it bites.
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
        // V14 §3.1, which CapacityModel already applies to the same pair: same broker, and what
        // differs is what it does with the fan-out. Written as the equality it is rather than as
        // "2", so the two can never be given different answers to the same question.
        Assert.Equal(LatencyModel.Queue, LatencyModel.Topic);
        Assert.Equal(CapacityModel.Queue, CapacityModel.Topic);
    }

    private static int? ConstantOf(string name) =>
        typeof(LatencyModel)
            .GetField(name, BindingFlags.Public | BindingFlags.Static)
            ?.GetRawConstantValue() as int?;
}
