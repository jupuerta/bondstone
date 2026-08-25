namespace arch_forge_backend.Application.Challenges;

public static class Difficulties
{
    public const string Beginner = "beginner";
    public const string Intermediate = "intermediate";
    public const string Advanced = "advanced";

    public static bool IsKnown(string difficulty) => difficulty is Beginner or Intermediate or Advanced;

    public static int Rank(string difficulty) => difficulty switch
    {
        Beginner => 0,
        Intermediate => 1,
        Advanced => 2,
        _ => 3
    };
}
