using System.Text.Json;
using System.Text.Json.Serialization;
using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using Xunit;

namespace arch_forge_backend.Application.Tests.Simulation;

/// <summary>
/// The shape that goes over the wire, checked against the same serializer options Program.cs
/// installs. Front and back deploy together and the TypeScript contract is written by hand from
/// these records (V2 D2.1), so a renamed field or an enum that stops travelling as a string is a
/// broken screen with a green build.
/// </summary>
public class SimulationContractTests
{
    private static readonly JsonSerializerOptions ApiOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void AScenarioArrivesFromTheBodyTheWayTheClientWritesIt()
    {
        // Exactly what api/architectureClient.ts sends, including the optional value being absent.
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
        // A fraction, not a percentage: the panel is what turns it into "43 %" (D1.6).
        Assert.Equal(0.4286m, metrics.GetProperty("blastRadius").GetDecimal());

        var contained = json.GetProperty("states").EnumerateArray()
            .Single(state => state.GetProperty("id").GetGuid() == graph.NodeId("Cache"));

        // The enums travel as strings, which is what the TypeScript unions are (D2.4's reasoning).
        Assert.Equal("Node", contained.GetProperty("type").GetString());
        Assert.Equal("Degraded", contained.GetProperty("state").GetString());
        Assert.Equal("fallback", contained.GetProperty("mechanism").GetString());
        Assert.Equal(1, contained.GetProperty("propagationStep").GetInt32());
        Assert.Equal("Connection", contained.GetProperty("causedBy").GetProperty("type").GetString());
    }

    [Fact]
    public void ADiscardedFaultTravelsBackAsTheClientWroteIt()
    {
        // Nothing else: the engine keys its indexes by element, but that is a convenience of the
        // engine's, and a third field restating the two above is a contract the client must ignore.
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
        // The canvas looks the element up instead of diffing against the graph (D2.2), so every
        // field has to be there even when there is nothing to say.
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
