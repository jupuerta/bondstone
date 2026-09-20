using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Common.Exceptions;
using bondstone_backend.Application.Common.Interfaces;
using bondstone_backend.Application.Configuration;
using bondstone_backend.Application.Simulation;

namespace bondstone_backend.Application.Architectures.Queries;

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

        var graph = new ArchitectureGraph(architecture.Nodes, architecture.Connections);

        return _engine.Run(graph, query.Scenario);
    }

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

            if (!seen.Add((fault.Target, fault.Id, fault.Kind)))
                throw new InvalidSimulationScenarioException($"Element '{fault.Id}' carries '{fault.Kind}' twice.");
        }

        var zones = new HashSet<string>(StringComparer.Ordinal);

        foreach (var zone in scenario.Zones)
        {
            if (!ConfigurationRanges.Zones.Contains(zone))
                throw new InvalidSimulationScenarioException($"'{zone}' is not a known zone.");

            if (!zones.Add(zone))
                throw new InvalidSimulationScenarioException($"Zone '{zone}' is listed twice.");
        }
    }
}
