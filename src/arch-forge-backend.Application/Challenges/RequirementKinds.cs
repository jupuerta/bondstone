namespace bondstone_backend.Application.Challenges;

public static class RequirementKinds
{

    public const string ContainsNode = "containsNode";

    public const string AbsentNode = "absentNode";

    public const string MaxFanOut = "maxFanOut";

    public const string AllEdgesTo = "allEdgesTo";

    public const string PathExists = "pathExists";

    public const string DeclaresLoad = "declaresLoad";

    public const string MaxNodes = "maxNodes";

    public const string MaxInstances = "maxInstances";

    public const string MaxCost = "maxCost";

    public const string Edge = "edge";

    public const string SpansZones = "spansZones";


    public const string NoFinding = "noFinding";

    public const string HasFinding = "hasFinding";

    public const string NoSeverity = "noSeverity";


    public const string UnderScenario = "underScenario";

    public static bool IsKnown(string kind) => kind is
        ContainsNode or AbsentNode or MaxFanOut or AllEdgesTo or PathExists or DeclaresLoad or
        MaxNodes or MaxInstances or MaxCost or Edge or SpansZones or
        NoFinding or HasFinding or NoSeverity or
        UnderScenario;
}

public static class RequirementMechanisms
{
    public const string Timeout = "timeout";
    public const string Retries = "retries";
    public const string CircuitBreaker = "circuitBreaker";
    public const string Fallback = "fallback";

    public static bool IsKnown(string mechanism) => mechanism is
        Timeout or Retries or CircuitBreaker or Fallback;
}

public static class RequirementMetrics
{
    public const string ComponentsDown = "componentsDown";
    public const string ComponentsDegraded = "componentsDegraded";
    public const string BlastRadius = "blastRadius";
    public const string EntryPointsAffected = "entryPointsAffected";
    public const string NominalLatencyMs = "nominalLatencyMs";
    public const string FaultedLatencyMs = "faultedLatencyMs";
    public const string RequestAmplification = "requestAmplification";

    public const string RequestsAffected = "requestsAffected";
    public const string BacklogRps = "backlogRps";
    public const string LoadAmplification = "loadAmplification";

    public static bool IsKnown(string metric) => metric is
        ComponentsDown or ComponentsDegraded or BlastRadius or EntryPointsAffected or
        NominalLatencyMs or FaultedLatencyMs or RequestAmplification or
        RequestsAffected or BacklogRps or LoadAmplification;
}

public static class RequirementOperators
{
    public const string LessThan = "lt";
    public const string LessOrEqual = "lte";
    public const string Equal = "eq";
    public const string GreaterOrEqual = "gte";
    public const string GreaterThan = "gt";

    public static bool IsKnown(string op) => op is
        LessThan or LessOrEqual or Equal or GreaterOrEqual or GreaterThan;

    public static bool Compare(decimal left, string op, decimal right) => op switch
    {
        LessThan => left < right,
        LessOrEqual => left <= right,
        Equal => left == right,
        GreaterOrEqual => left >= right,
        GreaterThan => left > right,
        _ => false
    };

}
