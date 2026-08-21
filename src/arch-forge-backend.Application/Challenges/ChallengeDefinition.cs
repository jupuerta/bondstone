namespace arch_forge_backend.Application.Challenges;

/// <summary>
/// V4 D1.5 and D4.3. A challenge as it is authored: a JSON file versioned in the repo, loaded and
/// validated once when the catalogue is built.
///
/// Not a database table, because D4.2 says users do not create challenges in V4, so a table would
/// buy edit-without-deploy and nothing else. A file travels with the code that evaluates it and
/// cannot get out of sync with it - the same reason the four reference architectures are files.
/// </summary>
public sealed class ChallengeDefinition
{
    /// <summary>Stable identity, and the URL: <c>/c/{slug}</c>.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Catalogue key of the title. V7 D1.1: the definition points at prose, it does not hold it.
    /// The slug is the identity and the key is derived from it by convention -
    /// <c>challenge.payments-friday.title</c> - so a definition and its text cannot drift apart
    /// without the catalogue test noticing.
    /// </summary>
    public string TitleKey { get; set; } = string.Empty;

    /// <summary>
    /// Catalogue key of the problem, told as a problem. Half of what makes §12 attractive is that
    /// these read like incidents.
    /// </summary>
    public string StoryKey { get; set; } = string.Empty;

    /// <summary>One of <see cref="Difficulties"/>, checked when the catalogue loads.</summary>
    public string Difficulty { get; set; } = string.Empty;

    /// <summary>
    /// V8 §6.5. Where this one sits <em>inside its difficulty band</em>. The catalogue is ordered
    /// by band and then by this, so the page reads as an itinerary - the challenge that teaches
    /// utilisation comes before the one that assumes you know it - instead of by slug, which is an
    /// order that means nothing to anybody.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// V8 §6.5. The axis it teaches, reusing the analyzer's <see cref="Analysis.Area"/> rather than
    /// inventing a second vocabulary for the same six words. It is a label for the catalogue page,
    /// never something the evaluator reads: a challenge is passed by its requirements.
    /// </summary>
    public Analysis.Area Topic { get; set; }

    /// <summary>
    /// D4.3, obligatory: <strong>a challenge that starts on an empty canvas is an exam, not a
    /// challenge</strong>. Names a blueprint next to this file.
    /// </summary>
    public string Starter { get; set; } = string.Empty;

    /// <summary>
    /// D3.5. An architecture that passes, and it is content and regression test at once: if a new
    /// rule or a tweak to a constant breaks a challenge, CI finds out instead of a user.
    /// Shown to whoever passes, never before - showing it on giving up turns the challenge into a
    /// tutorial.
    /// </summary>
    public string ReferenceSolution { get; set; } = string.Empty;

    /// <summary>All of these have to be met. The veredicto is binary (D1.3).</summary>
    public List<ChallengeRequirement> Requirements { get; set; } = [];

    /// <summary>These do not block. They are what makes it worth coming back (D1.3).</summary>
    public List<ChallengeRequirement> OptionalGoals { get; set; } = [];

    /// <summary>Catalogue keys, in the order they are handed out.</summary>
    public List<string> HintKeys { get; set; } = [];
}
