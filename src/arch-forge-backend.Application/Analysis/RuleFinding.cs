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
    IReadOnlyList<FindingElement> Elements)
{
    /// <summary>
    /// V4 D5.6. The correction, when there is exactly one and it is local and unambiguous - null on
    /// the ten rules whose fix is a decision rather than an edit. An optional property rather than a
    /// constructor parameter so that a rule earns one by opting in, and the other fourteen do not
    /// have to say `null`.
    /// </summary>
    public AppliedFix? Fix { get; init; }
}
