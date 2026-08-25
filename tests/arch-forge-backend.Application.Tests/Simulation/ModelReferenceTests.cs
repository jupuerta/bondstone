using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation;

/// <summary>
/// V16 C2. What is being defended here is not the endpoint's shape: it is that
/// <strong>publishing the model never becomes a second copy of it</strong>.
///
/// A table of capacities written down by hand in a controller — or in the frontend — is the failure
/// V16 Anexo B and Anexo C are both instances of: a number that lives in two places drifts, and it
/// drifts silently, because nothing compares the two. So every assertion below asks
/// <see cref="CapacityModel"/> and <see cref="LatencyModel"/> for the answer instead of stating it,
/// and the only literals in the file are the three the published sentence itself claims.
/// </summary>
public class ModelReferenceTests
{
    [Fact]
    public void Every_component_type_has_a_row_and_it_is_the_model_s_own()
    {
        // Totality, checked against the enum rather than against a list: a type added to NodeType
        // shows up on the screen with no second edit, which is the only way the table stays true.
        var rows = ModelReference.Snapshot.Components;

        Assert.Equal(Enum.GetValues<NodeType>().Length, rows.Count);

        foreach (var type in Enum.GetValues<NodeType>())
        {
            var row = Assert.Single(rows, candidate => candidate.Type == type);
            Assert.Equal(CapacityModel.PerInstance(type), row.CapacityPerInstance);
            Assert.Equal(LatencyModel.Processing(type), row.ProcessingMs);
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

        // DT.6: continuous at ρ = 1, where both branches are ×20 — which is why the cap can be
        // asked of the function instead of written down.
        Assert.Equal(CapacityModel.QueueingFactor(1m), curve.Cap);

        // Slowing down never reverses. A curve that dipped would be drawing a different model.
        for (var i = 1; i < curve.Points.Count; i++)
        {
            Assert.True(curve.Points[i].Utilization > curve.Points[i - 1].Utilization);
            Assert.True(curve.Points[i].Factor >= curve.Points[i - 1].Factor);
        }
    }

    [Fact]
    public void The_two_utilisations_worth_annotating_land_on_a_sample()
    {
        // The screen labels two points on this curve and reads their factor out of the payload
        // rather than recomputing it in TypeScript — a second implementation of QueueingFactor is
        // exactly the copy this file exists to prevent. Both have to be sampled exactly:
        // saturation, where the component turns Degraded, and the 172,5 % of CapacityModel's own
        // summary, where a call finally loses to a five-second timeout.
        var curve = ModelReference.Snapshot.Queueing;

        Assert.Contains(curve.Points, point => point.Utilization == CapacityModel.SaturationWarning);
        Assert.Contains(curve.Points, point => point.Utilization == 1.725m);
    }

    [Fact]
    public void The_sentence_the_screen_publishes_is_still_true()
    {
        // "The database is the bottleneck by design: twenty-five times more fragile than a cache
        // and ten times more than a gateway" is CapacityModel's own header comment, and V16 C2
        // puts it on screen as the lesson the table teaches. Move one of the three numbers and the
        // published sentence becomes false — which is a worse failure than the table being wrong,
        // because a ratio in prose is the part nobody re-derives.
        Assert.Equal(25, CapacityModel.Cache / CapacityModel.Database);
        Assert.Equal(10, CapacityModel.Gateway / CapacityModel.Database);
    }
}
