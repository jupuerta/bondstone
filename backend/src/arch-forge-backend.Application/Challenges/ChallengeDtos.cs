using bondstone_backend.Application.Localization;

namespace bondstone_backend.Application.Challenges;

public sealed record ChallengeSummaryDto(
    string Slug,
    LocalizedText Title,
    LocalizedText Story,
    string Difficulty,
    Analysis.Area Topic,
    int RequirementCount);

public sealed record ChallengeDetailDto(
    string Slug,
    LocalizedText Title,
    LocalizedText Story,
    string Difficulty,
    Analysis.Area Topic,
    IReadOnlyList<ChallengeRequirementDto> Requirements,
    IReadOnlyList<ChallengeRequirementDto> OptionalGoals,
    IReadOnlyList<LocalizedText> Hints);

public sealed record ChallengeRequirementDto(string Kind, LocalizedText Label, int? LatencyBudgetMs = null);

public static class ChallengeMappingExtensions
{
    public static ChallengeSummaryDto ToSummaryDto(this ChallengeDefinition definition) => new(
        definition.Slug,
        LocalizedText.Of(definition.TitleKey),
        LocalizedText.Of(definition.StoryKey),
        definition.Difficulty,
        definition.Topic,
        definition.Requirements.Count);

    public static ChallengeDetailDto ToDetailDto(this ChallengeDefinition definition) => new(
        definition.Slug,
        LocalizedText.Of(definition.TitleKey),
        LocalizedText.Of(definition.StoryKey),
        definition.Difficulty,
        definition.Topic,
        definition.Requirements.Select(Map).ToList(),
        definition.OptionalGoals.Select(Map).ToList(),
        definition.HintKeys.Select(LocalizedText.Of).ToList());

    private static ChallengeRequirementDto Map(ChallengeRequirement requirement) =>
        new(requirement.Kind, LocalizedText.Of(requirement.LabelKey), LatencyBudgetOf(requirement));

    private static int? LatencyBudgetOf(ChallengeRequirement requirement)
    {
        if (requirement.Kind != RequirementKinds.UnderScenario) return null;

        if (requirement.Metric is not (RequirementMetrics.NominalLatencyMs or RequirementMetrics.FaultedLatencyMs))
            return null;

        if (requirement.Operator is not (RequirementOperators.LessThan or RequirementOperators.LessOrEqual))
            return null;

        if (requirement.Faults is { Count: > 0 }) return null;

        return requirement.Value is { } value ? (int)value : null;
    }
}
