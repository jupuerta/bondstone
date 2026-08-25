using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis;

public sealed record RuleFinding(
    string Code,
    Area Area,
    Severity Severity,
    LocalizedText Message,
    LocalizedText Explanation,
    LocalizedText? Suggestion,
    IReadOnlyList<FindingElement> Elements)
{
    public AppliedFix? Fix { get; init; }
}
