using bondstone_backend.Application.Localization;

namespace bondstone_backend.Application.Analysis;

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
