using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Simulation;

namespace bondstone_backend.Application.Challenges;

public sealed class EvaluationContext
{
    private readonly Func<SimulationScenario, SimulationResult> _simulate;
    private readonly Dictionary<string, SimulationResult> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _idsByName;

    public EvaluationContext(
        ArchitectureGraph graph,
        AnalysisResult analysis,
        Func<SimulationScenario, SimulationResult> simulate)
    {
        Graph = graph;
        Analysis = analysis;
        _simulate = simulate;

        _idsByName = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
            _idsByName[node.Name] = node.Id;
    }

    public ArchitectureGraph Graph { get; }
    public AnalysisResult Analysis { get; }

    public int SimulationsRun => _cache.Count;

    // A node is resolved to its Guid here, because that is the only place that knows the names on this
    // board. A zone is not: it travels on as a name and SimulationScenario.Resolve expands it against
    // the graph, which is what keeps the scenario true when a component joins the zone later.
    public bool TryResolve(IReadOnlyList<ScenarioFault> faults, out SimulationScenario scenario, out string? missing)
    {
        var resolved = new List<Fault>(faults.Count);
        var zones = new List<string>();

        foreach (var fault in faults)
        {
            if (fault.NamesAZone)
            {
                zones.Add(fault.Zone!);
                continue;
            }

            if (!_idsByName.TryGetValue(fault.Node!, out var id))
            {
                scenario = SimulationScenario.Empty;
                missing = fault.Node;
                return false;
            }

            resolved.Add(new Fault(ElementType.Node, id, fault.Kind, fault.Value));
        }

        scenario = new SimulationScenario(resolved, zones);
        missing = null;
        return true;
    }

    public SimulationResult Simulate(SimulationScenario scenario)
    {
        var canonical = scenario.Canonical();
        var key = string.Join("|",
            canonical.Faults.Select(f => $"{f.Target}:{f.Id}:{f.Kind}:{f.Value}")
                .Concat(canonical.Zones.Select(z => $"zone:{z}")));

        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var result = _simulate(canonical);
        _cache[key] = result;
        return result;
    }
}
