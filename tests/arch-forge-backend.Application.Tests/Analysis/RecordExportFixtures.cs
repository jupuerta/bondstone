using System.Text.Json;
using System.Text.Json.Serialization;
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
/// </summary>
public class RecordExportFixtures
{
    private const string Dir = "../../../../../../arch-forge-frontend/src/export/__fixtures__";

    [Fact(Skip = "Generator: writes into the frontend repository. Run by hand when CurrentScoringVersion moves.")]
    public void Record()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());

        foreach (var name in new[] { "minimal-api-db", "resilient-async", "gateway-microservices", "worst-case" })
        {
            var result = ReferenceArchitecture.Analyze(name);
            var path = Path.GetFullPath(Path.Combine(Dir, $"{name}.analysis.json"));

            File.WriteAllText(path, JsonSerializer.Serialize(result, options) + "\n");
        }
    }
}
