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

    public string Title { get; set; } = string.Empty;

    /// <summary>The problem, told as a problem. Half of what makes §12 attractive is that these read like incidents.</summary>
    public string Story { get; set; } = string.Empty;

    public string Difficulty { get; set; } = string.Empty;

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

    public List<string> Hints { get; set; } = [];
}
