namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// V2 D2.1. Three separate texts rather than one paragraph, because the panel lays them out
/// differently (D2.3): <see cref="Message"/> is the headline, <see cref="Explanation"/> collapses,
/// <see cref="Suggestion"/> gets its own treatment and is null on Good findings.
/// </summary>
public sealed record RuleFinding(
    string Code,
    Area Area,
    Severity Severity,
    string Message,
    string Explanation,
    string? Suggestion,
    IReadOnlyList<FindingElement> Elements);
