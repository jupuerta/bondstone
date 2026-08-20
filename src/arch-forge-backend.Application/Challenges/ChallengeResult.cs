using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Challenges;

/// <summary>
/// V4 D3.3. The result of one requirement, and the reason this feature is worth building:
/// <strong>a bare "not passed" teaches nothing</strong>.
/// </summary>
/// <param name="Detail">
/// Why not, with the real number in it. Null when the requirement is met - there is nothing to
/// explain about something that works.
///
/// V7 D1.1: a key and its arguments, and some of those arguments are themselves a
/// <see cref="LocalizedText"/> - a failed <c>noFinding</c> quotes the finding's own sentence, and
/// quoting it means resolving it in the same language as the sentence around it, not pasting in
/// prose somebody else already rendered.
/// </param>
/// <param name="Elements">
/// What to look at. Same flat shape as <see cref="RuleFinding.Elements"/> (V2 D2.2), so the canvas
/// lights them up by iterating and does not need to know a challenge produced them. This is what
/// makes a failed requirement <em>clickable</em>.
/// </param>
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

/// <summary>
/// V4 D1.3 and D3.4. The whole veredicto, and <strong>nothing of it is persisted</strong>: it is a
/// pure function of a saved architecture and a definition in the repo, exactly like an analysis
/// (V2 D5.3) and a simulation (V3 D3.6). Progress lives in the browser and is recomputed on open,
/// which is also what keeps D1.4 from biting: a stored veredicto can go stale when a rule lands, a
/// recomputed one cannot.
/// </summary>
/// <param name="Passed">Every mandatory requirement met. Optional goals never block.</param>
public sealed record ChallengeResult(
    string Slug,
    bool Passed,
    IReadOnlyList<ChallengeRequirementResult> Requirements,
    IReadOnlyList<ChallengeRequirementResult> OptionalGoals);
