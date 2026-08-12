using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Challenges;

/// <summary>
/// Everything <see cref="RequirementEvaluator"/> is allowed to look at, and nothing else.
///
/// It takes a <see cref="Func{T, TResult}"/> for the simulation rather than the engine itself so a
/// requirement can be tested without a repository and without a scenario being built by hand -
/// which is the same reason <c>EdgeEffect</c> and <c>CallCost</c> live outside the engine in V3.
/// </summary>
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

        // Last one wins on a duplicate name rather than throwing: names are the user's to choose
        // and nothing stops two components sharing one. A challenge whose fault becomes ambiguous
        // is a challenge that will not pass, which the user can see and fix.
        _idsByName = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
            _idsByName[node.Name] = node.Id;
    }

    public ArchitectureGraph Graph { get; }
    public AnalysisResult Analysis { get; }

    /// <summary>How many distinct scenarios were actually run. The cap of D3.2 is enforced when the catalogue loads; this is what a test asserts on.</summary>
    public int SimulationsRun => _cache.Count;

    /// <summary>
    /// Turns the definition's faults, which point at components <em>by name</em>
    /// (<see cref="ScenarioFault"/>), into a scenario over this architecture's ids.
    /// </summary>
    public bool TryResolve(IReadOnlyList<ScenarioFault> faults, out SimulationScenario scenario, out string? missing)
    {
        var resolved = new List<Fault>(faults.Count);

        foreach (var fault in faults)
        {
            if (!_idsByName.TryGetValue(fault.Node, out var id))
            {
                scenario = SimulationScenario.Empty;
                missing = fault.Node;
                return false;
            }

            resolved.Add(new Fault(ElementType.Node, id, fault.Kind, fault.Value));
        }

        scenario = new SimulationScenario(resolved);
        missing = null;
        return true;
    }

    /// <summary>
    /// D3.2: requirements that name the same scenario share one run. Three requirements over
    /// "with Payments down" cost one simulation, not three.
    /// </summary>
    public SimulationResult Simulate(SimulationScenario scenario)
    {
        var canonical = scenario.Canonical();
        var key = string.Join("|", canonical.Faults.Select(f => $"{f.Target}:{f.Id}:{f.Kind}:{f.Value}"));

        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var result = _simulate(canonical);
        _cache[key] = result;
        return result;
    }
}
