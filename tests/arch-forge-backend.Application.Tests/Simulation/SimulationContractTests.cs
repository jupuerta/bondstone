using System.Text.Json;
using System.Text.Json.Serialization;
using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Simulation;
using bondstone_backend.Application.Tests.Analysis.Fixtures;
using Xunit;

namespace bondstone_backend.Application.Tests.Simulation;

public class SimulationContractTests
{
    private static readonly JsonSerializerOptions ApiOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void AScenarioArrivesFromTheBodyTheWayTheClientWritesIt()
    {
        const string body = """
            {
              "faults": [
                { "target": "Node", "id": "a0000000-0000-0000-0000-000000000002", "kind": "down" },
                { "target": "Connection", "id": "c0000000-0000-0000-0000-000000000001", "kind": "latency", "value": 8000 }
              ]
            }
            """;

        var request = JsonSerializer.Deserialize<Body>(body, ApiOptions);

        Assert.NotNull(request);
        Assert.Equal(2, request!.Faults.Count);

        Assert.Equal(ElementType.Node, request.Faults[0].Target);
        Assert.Equal(FaultKinds.Down, request.Faults[0].Kind);
        Assert.Null(request.Faults[0].Value);

        Assert.Equal(ElementType.Connection, request.Faults[1].Target);
        Assert.Equal(8_000, request.Faults[1].Value);
    }

    [Fact]
    public void TheResultSerializesWithTheNamesAndTypesTheFrontendReads()
    {
        var graph = ReferenceArchitecture.Graph("resilient-async");
        var result = new SimulationEngine().Run(
            graph,
            new SimulationScenario([new Fault(ElementType.Node, graph.NodeId("Db"), FaultKinds.Down)]));

        var json = JsonSerializer.SerializeToElement(result, ApiOptions);

        Assert.Equal("v3", json.GetProperty("simulationVersion").GetString());
        Assert.Equal(0, json.GetProperty("discardedFaults").GetArrayLength());

        var metrics = json.GetProperty("metrics");
        Assert.Equal(1, metrics.GetProperty("componentsDown").GetInt32());
        Assert.Equal(17, metrics.GetProperty("faultedLatencyMs").GetInt32());
        Assert.Equal(0.4286m, metrics.GetProperty("blastRadius").GetDecimal());

        var contained = json.GetProperty("states").EnumerateArray()
            .Single(state => state.GetProperty("id").GetGuid() == graph.NodeId("Cache"));

        Assert.Equal("Node", contained.GetProperty("type").GetString());
        Assert.Equal("Degraded", contained.GetProperty("state").GetString());
        Assert.Equal("fallback", contained.GetProperty("mechanism").GetString());
        Assert.Equal(1, contained.GetProperty("propagationStep").GetInt32());
        Assert.Equal("Connection", contained.GetProperty("causedBy").GetProperty("type").GetString());
    }

    [Fact]
    public void ADiscardedFaultTravelsBackAsTheClientWroteIt()
    {
        var graph = ReferenceArchitecture.Graph("minimal-api-db");
        var ghost = new Fault(ElementType.Node, Guid.NewGuid(), FaultKinds.Down);

        var result = new SimulationEngine().Run(graph, new SimulationScenario([ghost]));
        var json = JsonSerializer.SerializeToElement(result, ApiOptions);

        var discarded = json.GetProperty("discardedFaults").EnumerateArray().Single();

        Assert.Equal(["target", "id", "kind", "value"], discarded.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Node", discarded.GetProperty("target").GetString());
        Assert.Equal("down", discarded.GetProperty("kind").GetString());
    }

    [Fact]
    public void AHealthyElementCarriesANullMechanismRatherThanNoField()
    {
        var graph = ReferenceArchitecture.Graph("minimal-api-db");
        var result = new SimulationEngine().Run(graph, SimulationScenario.Empty);

        var json = JsonSerializer.SerializeToElement(result, ApiOptions);
        var state = json.GetProperty("states").EnumerateArray().First();

        Assert.Equal(JsonValueKind.Null, state.GetProperty("mechanism").ValueKind);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("causedBy").ValueKind);
        Assert.Equal("Healthy", state.GetProperty("state").GetString());
    }

    private sealed record Body(List<Fault> Faults);
}
