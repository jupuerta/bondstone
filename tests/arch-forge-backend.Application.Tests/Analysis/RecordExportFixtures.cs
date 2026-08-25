using System.Text.Json;
using System.Text.Json.Serialization;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis;

public class RecordExportFixtures
{
    private const string Dir = "../../../../../../arch-forge-frontend/src/export/__fixtures__";

    private static readonly JsonSerializerOptions Options = Web();

    [Fact(Skip = "Generator: writes into the frontend repository. Run by hand when CurrentScoringVersion moves.")]
    public void Record()
    {
        foreach (var name in new[] { "minimal-api-db", "resilient-async", "gateway-microservices", "worst-case" })
            Write($"{name}.analysis.json", ReferenceArchitecture.Analyze(name));

        Write("minimal-api-db.down.simulation.json", Simulated("minimal-api-db", "minimal-api-db.down.faults.json"));
    }

    private static SimulationResult Simulated(string fixtureName, string faultsFile)
    {
        var faults = JsonSerializer.Deserialize<List<Fault>>(
            File.ReadAllText(Path.GetFullPath(Path.Combine(Dir, faultsFile))), Options)!;

        return new SimulationEngine().Run(ReferenceArchitecture.Graph(fixtureName), new SimulationScenario(faults));
    }

    private static void Write<T>(string name, T payload) =>
        File.WriteAllText(
            Path.GetFullPath(Path.Combine(Dir, name)),
            JsonSerializer.Serialize(payload, Options) + "\n");

    private static JsonSerializerOptions Web()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
