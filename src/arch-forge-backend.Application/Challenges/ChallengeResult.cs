using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Challenges;

public sealed record ChallengeRequirementResult(
    string Kind,
    LocalizedText Label,
    bool Met,
    LocalizedText? Detail,
    IReadOnlyList<FindingElement> Elements)
{
    public static ChallengeRequirementResult Pass(ChallengeRequirement requirement) =>
        new(requirement.Kind, LocalizedText.Of(requirement.LabelKey), true, null, []);

    public static ChallengeRequirementResult Fail(
        ChallengeRequirement requirement,
        LocalizedText detail,
        IReadOnlyList<FindingElement>? elements = null) =>
        new(requirement.Kind, LocalizedText.Of(requirement.LabelKey), false, detail, elements ?? []);
}

public sealed record ChallengeResult(
    string Slug,
    bool Passed,
    IReadOnlyList<ChallengeRequirementResult> Requirements,
    IReadOnlyList<ChallengeRequirementResult> OptionalGoals);
