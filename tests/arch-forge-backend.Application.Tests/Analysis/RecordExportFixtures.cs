using System.Text.Json;
using System.Text.Json.Serialization;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Application.Tests.Analysis.Fixtures;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis;

/// <summary>
/// V15 cierre. <strong>The generator of the four <c>*.analysis.json</c> recordings</strong> in
/// <c>arch-forge-frontend/src/export/__fixtures__</c> - the <c>/analyze</c> payload of each
/// reference architecture, exactly as the API serializes it, which the exporter's golden files
/// are composed from and which <c>check-catalogs.mjs</c> reads for the keys the backend emits.
///
/// <strong>Why this exists.</strong> Every scoring bump regenerates them, and until v8 that was
/// done "in process" by whoever closed the pass, with no committed way to do it again. V14's
/// closing note documents the procedure (the API's serializer: <c>JsonSerializerDefaults.Web</c>
/// plus <c>JsonStringEnumConverter</c>, which is what <c>Program.cs</c> configures); this is that
/// procedure as a file, next to <see cref="RecordAnalyzerSentences"/>, which does the same for the
/// sentence recording.
///
/// <strong>Skipped on purpose</strong>, for the same reason: it writes into the other repository.
/// Remove the <c>Skip</c> and run it when <c>CurrentScoringVersion</c> moves; then regenerate the
/// six <c>.export.md</c> golden files with <c>npm run regenerate:goldens</c> on the frontend, which
/// composes them from these.
///
/// The <c>.architecture.json</c> files are not touched: they are the drawings, and a scoring
/// version changes what a drawing scores, never the drawing.
///
/// <strong>V17 A: the fifth recording joined it.</strong> <c>minimal-api-db.down.simulation.json</c>
/// is the <c>/simulate</c> payload the golden document of the fifth reference is composed from, and
/// until now it had <em>no generator at all</em> - it was written once and edited by hand
/// afterwards, which is the state this file exists to end. It is produced here, from the same
/// drawing and the same <c>.down.faults.json</c> the exporter replays, because the two payloads
/// travel together: a field added to either of the two contracts regenerates the same directory,
/// and two agents regenerating one directory is the conflict V17's plan puts them both in one
/// commission to avoid (§2).
/// </summary>
public class RecordExportFixtures
{
    private const string Dir = "../../../../../../arch-forge-frontend/src/export/__fixtures__";

    /// <summary>The API's serializer, which is what <c>Program.cs</c> configures.</summary>
    private static readonly JsonSerializerOptions Options = Web();

    [Fact(Skip = "Generator: writes into the frontend repository. Run by hand when CurrentScoringVersion moves.")]
    public void Record()
    {
        foreach (var name in new[] { "minimal-api-db", "resilient-async", "gateway-microservices", "worst-case" })
            Write($"{name}.analysis.json", ReferenceArchitecture.Analyze(name));

        Write("minimal-api-db.down.simulation.json", Simulated("minimal-api-db", "minimal-api-db.down.faults.json"));
    }

    /// <summary>
    /// The scenario is read from the recording beside it rather than written out here: the faults
    /// file is what the exporter replays, so a recording produced from a different list of faults
    /// would be a document nobody can reproduce.
    /// </summary>
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
