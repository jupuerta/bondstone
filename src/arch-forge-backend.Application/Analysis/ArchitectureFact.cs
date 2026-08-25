namespace arch_forge_backend.Application.Analysis;

public abstract record ArchitectureFact;

public sealed record SinglePointOfFailureFact(Guid NodeId, Severity Severity) : ArchitectureFact;

public sealed record CyclicConnectionFact(Guid ConnectionId) : ArchitectureFact;

public sealed record RetryAmplificationFact(Guid ConnectionId) : ArchitectureFact;

public sealed record ExternalNoFallbackFact(Guid ConnectionId) : ArchitectureFact;

public sealed record UndersizedComponentFact(Guid NodeId, Severity Severity) : ArchitectureFact;
