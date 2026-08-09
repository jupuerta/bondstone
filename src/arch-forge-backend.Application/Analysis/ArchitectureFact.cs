namespace arch_forge_backend.Application.Analysis;

public abstract record ArchitectureFact(Guid NodeId);

public sealed record SinglePointOfFailureFact(Guid NodeId) : ArchitectureFact(NodeId);
