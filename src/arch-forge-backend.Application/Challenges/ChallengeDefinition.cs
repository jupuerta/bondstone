namespace arch_forge_backend.Application.Challenges;

public sealed class ChallengeDefinition
{
    public string Slug { get; set; } = string.Empty;

    public string TitleKey { get; set; } = string.Empty;

    public string StoryKey { get; set; } = string.Empty;

    public string Difficulty { get; set; } = string.Empty;

    public int Order { get; set; }

    public Analysis.Area Topic { get; set; }

    public string Starter { get; set; } = string.Empty;

    public string ReferenceSolution { get; set; } = string.Empty;

    public List<ChallengeRequirement> Requirements { get; set; } = [];

    public List<ChallengeRequirement> OptionalGoals { get; set; } = [];

    public List<string> HintKeys { get; set; } = [];
}
