using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Challenges;

/// <summary>
/// What the catalogue page shows. Neither DTO carries the names of the blueprint files: they are a
/// detail of how the definition is stored, and the reference solution is not something a client
/// should be able to ask for by guessing a path (D3.5).
/// </summary>
public sealed record ChallengeSummaryDto(
    string Slug,
    LocalizedText Title,
    LocalizedText Story,
    string Difficulty,
    int RequirementCount);

/// <summary>
/// D3.6: the requirements travel <strong>before</strong> anyone evaluates anything. Without a
/// ranking, building while looking at the checklist is not cheating - the checklist is the
/// enunciado broken into pieces, and hiding it would only add a guessing game.
/// </summary>
public sealed record ChallengeDetailDto(
    string Slug,
    LocalizedText Title,
    LocalizedText Story,
    string Difficulty,
    IReadOnlyList<ChallengeRequirementDto> Requirements,
    IReadOnlyList<ChallengeRequirementDto> OptionalGoals,
    IReadOnlyList<LocalizedText> Hints);

public sealed record ChallengeRequirementDto(string Kind, LocalizedText Label);

public static class ChallengeMappingExtensions
{
    public static ChallengeSummaryDto ToSummaryDto(this ChallengeDefinition definition) => new(
        definition.Slug,
        LocalizedText.Of(definition.TitleKey),
        LocalizedText.Of(definition.StoryKey),
        definition.Difficulty,
        definition.Requirements.Count);

    public static ChallengeDetailDto ToDetailDto(this ChallengeDefinition definition) => new(
        definition.Slug,
        LocalizedText.Of(definition.TitleKey),
        LocalizedText.Of(definition.StoryKey),
        definition.Difficulty,
        definition.Requirements.Select(Map).ToList(),
        definition.OptionalGoals.Select(Map).ToList(),
        definition.HintKeys.Select(LocalizedText.Of).ToList());

    private static ChallengeRequirementDto Map(ChallengeRequirement requirement) =>
        new(requirement.Kind, LocalizedText.Of(requirement.LabelKey));
}
