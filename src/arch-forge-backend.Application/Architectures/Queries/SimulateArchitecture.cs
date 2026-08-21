using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Common.Exceptions;
using arch_forge_backend.Application.Common.Interfaces;
using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Architectures.Queries;

/// <summary>
/// V3 D2.4. Sibling of <see cref="AnalyzeArchitectureQueryHandler"/>, and a query rather than a
/// command for the same reason: it persists nothing (D3.6). It reads the <em>saved</em>
/// architecture exactly as <c>/analyze</c> does - sending the graph in the body would open a second
/// door into the engine and a second way for the client's graph and the stored one to disagree,
/// and V2 already solved that with <c>savedRevision</c>.
/// </summary>
public class SimulateArchitectureQuery
{
    public string ShareToken { get; set; } = string.Empty;
    public SimulationScenario Scenario { get; set; } = SimulationScenario.Empty;
}

public class SimulateArchitectureQueryHandler
{
    private readonly IArchitectureRepository _repository;
    private readonly SimulationEngine _engine;

    public SimulateArchitectureQueryHandler(IArchitectureRepository repository, SimulationEngine engine)
    {
        _repository = repository;
        _engine = engine;
    }

    public async Task<SimulationResult?> Handle(SimulateArchitectureQuery query, CancellationToken cancellationToken = default)
    {
        Validate(query.Scenario);

        var architecture = await _repository.GetByShareTokenAsync(query.ShareToken, cancellationToken);
        if (architecture is null)
            return null;

        // The same ArchitectureGraph V2 built, with no rule run over it: a simulation must not
        // depend on which rules happen to be registered (D5.2).
        var graph = new ArchitectureGraph(architecture.Nodes, architecture.Connections);

        return _engine.Run(graph, query.Scenario);
    }

    /// <summary>
    /// D2.1 draws the distinction that matters here, because these are two failures that look
    /// alike: an unknown <c>kind</c> is a malformed request and gets a 400, while an id that no
    /// longer exists is an old link over an edited architecture and must still open - the engine
    /// drops it and the UI says so.
    /// </summary>
    private static void Validate(SimulationScenario scenario)
    {
        var seen = new HashSet<(ElementType, Guid, string)>();

        foreach (var fault in scenario.Faults)
        {
            if (!FaultKinds.IsKnown(fault.Kind))
                throw new InvalidSimulationScenarioException($"'{fault.Kind}' is not a known fault kind.");

            if (!FaultKinds.AppliesTo(fault.Kind, fault.Target))
                throw new InvalidSimulationScenarioException($"'{fault.Kind}' cannot be applied to a {fault.Target}.");

            if (FaultKinds.RequiresValue(fault.Kind) && fault.Value is not > 0)
                throw new InvalidSimulationScenarioException($"'{fault.Kind}' needs a value greater than 0.");

            // Two faults of the same kind over the same element would make the parameter ambiguous.
            if (!seen.Add((fault.Target, fault.Id, fault.Kind)))
                throw new InvalidSimulationScenarioException($"Element '{fault.Id}' carries '{fault.Kind}' twice.");
        }
    }
}
