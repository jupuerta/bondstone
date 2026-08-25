namespace arch_forge_backend.Application.Analysis;

public sealed record ComponentLoad(Guid NodeId, decimal Rps, decimal CapacityRps, decimal Utilization);
