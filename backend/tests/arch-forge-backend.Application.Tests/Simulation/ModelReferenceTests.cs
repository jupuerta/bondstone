using bondstone_backend.Application.Simulation;
using bondstone_backend.Domain.Enums;
using Xunit;

namespace bondstone_backend.Application.Tests.Simulation;

public class ModelReferenceTests
{
    [Fact]
    public void Every_component_type_has_a_row_and_it_is_the_model_s_own()
    {
        var rows = ModelReference.Snapshot.Components;

        Assert.Equal(Enum.GetValues<NodeType>().Length, rows.Count);

        foreach (var type in Enum.GetValues<NodeType>())
        {
            var row = Assert.Single(rows, candidate => candidate.Type == type);
            Assert.Equal(CapacityModel.PerInstance(type), row.CapacityPerInstance);
            Assert.Equal(LatencyModel.Processing(type), row.ProcessingMs);
            Assert.Equal(CostModel.PerInstance(type), row.CostPerInstance);
        }
    }

    [Fact]
    public void Every_protocol_carries_its_hop()
    {
        var hops = ModelReference.Snapshot.Network;

        Assert.Equal(Enum.GetValues<ProtocolType>().Length, hops.Count);

        foreach (var protocol in Enum.GetValues<ProtocolType>())
            Assert.Equal(LatencyModel.Hop(protocol), Assert.Single(hops, hop => hop.Protocol == protocol).Ms);
    }

    [Fact]
    public void The_thresholds_are_the_ones_the_analyzer_reads()
    {
        var model = ModelReference.Snapshot;

        Assert.Equal(CapacityModel.SaturationWarning, model.SaturationWarning);
        Assert.Equal(LatencyModel.UnboundedWait, model.UnboundedWaitMs);
        Assert.Equal(CapacityModel.DefaultHitRatio, model.DefaultHitRatio);
        Assert.Equal(CapacityModel.DefaultCdnHitRatio, model.DefaultCdnHitRatio);
    }

    [Fact]
    public void The_curve_is_the_function_and_not_a_drawing_of_it()
    {
        var curve = ModelReference.Snapshot.Queueing;

        Assert.All(curve.Points, point => Assert.Equal(CapacityModel.QueueingFactor(point.Utilization), point.Factor));

        Assert.Equal(CapacityModel.QueueingFactor(1m), curve.Cap);

        for (var i = 1; i < curve.Points.Count; i++)
        {
            Assert.True(curve.Points[i].Utilization > curve.Points[i - 1].Utilization);
            Assert.True(curve.Points[i].Factor >= curve.Points[i - 1].Factor);
        }
    }

    [Fact]
    public void The_two_utilisations_worth_annotating_land_on_a_sample()
    {
        var curve = ModelReference.Snapshot.Queueing;

        Assert.Contains(curve.Points, point => point.Utilization == CapacityModel.SaturationWarning);
        Assert.Contains(curve.Points, point => point.Utilization == 1.725m);
    }

    [Fact]
    public void The_sentence_the_screen_publishes_is_still_true()
    {
        Assert.Equal(25, CapacityModel.Cache / CapacityModel.Database);
        Assert.Equal(10, CapacityModel.Gateway / CapacityModel.Database);
    }
}
