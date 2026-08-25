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
    Analysis.Area Topic,
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
    Analysis.Area Topic,
    IReadOnlyList<ChallengeRequirementDto> Requirements,
    IReadOnlyList<ChallengeRequirementDto> OptionalGoals,
    IReadOnlyList<LocalizedText> Hints);

/// <param name="Kind">From the closed catalogue. The client renders the checklist off it and never re-implements the predicate.</param>
/// <param name="Label">What the user reads.</param>
/// <param name="LatencyBudgetMs">
/// V17 D1. The one operand that travels, and only for the one requirement that is a line on a
/// drawing: the milliseconds a latency requirement is asking you to come in under.
///
/// <strong>Why this is not the general case.</strong> A requirement's parameters deliberately do not
/// travel (D3.6): the checklist is rendered from a kind and a label so that the client cannot drift
/// into evaluating it, and publishing <c>Value</c>, <c>Operator</c> and <c>Metric</c> would hand it
/// everything it needs to try. This is the exception because a latency budget is not something the
/// client evaluates - the verdict still comes from <see cref="EvaluateChallenge"/> - it is
/// something it <em>draws</em>: the cascade of D1 puts it on the axis as a vertical line, and
/// "where you go over it" is a place on the picture rather than a subtraction.
///
/// Null on every requirement that is not exactly that, and the conditions are all four:
/// <list type="bullet">
///   <item>the kind is <see cref="RequirementKinds.UnderScenario"/>;</item>
///   <item>the metric is one of the two latencies - the others are counts and percentages, and a
///     vertical line on a millisecond axis would be nonsense;</item>
///   <item>the operator is a ceiling (<c>lt</c> / <c>lte</c>). A floor is not a budget: nobody is
///     asking you to be <em>slower</em> than 100 ms, and drawing one as if they were would invert
///     the reading of the whole cascade;</item>
///   <item><strong>the scenario is empty.</strong> A requirement graded under injected faults is
///     graded on a different walk from the one on screen unless the user happens to have
///     reproduced it exactly, and the client cannot know that: a challenge's faults point at
///     components by name and the canvas holds ids. Two empty scenarios agree without anyone
///     comparing them, which makes this a condition rather than a special case - the same shape as
///     Anexo A's second decision. It is why <c>retry-storm</c> and <c>third-party-outage</c>, whose
///     optional goals are latency budgets under a fault, publish nothing here.</item>
/// </list>
/// </param>
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

    /// <summary>V17 D1. See <see cref="ChallengeRequirementDto.LatencyBudgetMs"/> for all four conditions.</summary>
    private static int? LatencyBudgetOf(ChallengeRequirement requirement)
    {
        if (requirement.Kind != RequirementKinds.UnderScenario) return null;

        if (requirement.Metric is not (RequirementMetrics.NominalLatencyMs or RequirementMetrics.FaultedLatencyMs))
            return null;

        if (requirement.Operator is not (RequirementOperators.LessThan or RequirementOperators.LessOrEqual))
            return null;

        if (requirement.Faults is { Count: > 0 }) return null;

        // Milliseconds are whole here and everywhere else the product prints one; the decimal is
        // the definition format's, not the axis's.
        return requirement.Value is { } value ? (int)value : null;
    }
}
