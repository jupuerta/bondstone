namespace arch_forge_backend.Application.Analysis;

public sealed record ComponentCost(Guid NodeId, int PerInstance, int Instances, int Total);

public sealed record ArchitectureCost(int Total, IReadOnlyList<ComponentCost> Components);
