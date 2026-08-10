namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// <see cref="Passed"/> counts applicable rules that found nothing (V2 D1.4), so it measures what
/// it claims to measure instead of rewarding an architecture too small to check.
/// </summary>
public sealed record AnalysisSummary(int Critical, int Warning, int Passed);
