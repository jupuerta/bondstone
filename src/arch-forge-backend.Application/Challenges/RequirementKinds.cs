namespace arch_forge_backend.Application.Challenges;

/// <summary>
/// V4 D3.1, the closed catalogue. Three families and nothing else:
///
/// <list type="bullet">
/// <item><description><strong>Estructural</strong> - a predicate over the graph.</description></item>
/// <item><description><strong>Analítica</strong> - over the <see cref="Analysis.AnalysisResult"/> that already exists.</description></item>
/// <item><description><strong>Simulada</strong> - over a <see cref="Simulation.SimulationResult"/> under a named scenario.</description></item>
/// </list>
///
/// The two latter families need no engine of their own: they are comparisons over two contracts
/// that are already deterministic and already tested. That is what makes the evaluator a
/// <c>switch</c> instead of a third motor.
///
/// Kinds are validated when the catalogue loads, not when a challenge is evaluated: a malformed
/// definition is a deploy-time mistake and should never reach a user (<see cref="ChallengeCatalog"/>).
/// </summary>
public static class RequirementKinds
{
    // ---- Estructural -------------------------------------------------------

    /// <summary>At least <c>Min</c> nodes of <c>NodeType</c>. The plainest requirement there is.</summary>
    public const string ContainsNode = "containsNode";

    /// <summary>No node of <c>NodeType</c>. For the challenges whose lesson is removing something.</summary>
    public const string AbsentNode = "absentNode";

    /// <summary>No node calls more than <c>Value</c> components directly.</summary>
    public const string MaxFanOut = "maxFanOut";

    /// <summary>
    /// Every <em>synchronous</em> call into a node of <c>NodeType</c> carries <c>Mechanism</c>.
    /// Synchronous only, and that is D4.2 of V3 rather than a shortcut: on an asynchronous edge
    /// nobody is waiting, so there is no error for a mechanism to catch.
    /// </summary>
    public const string AllEdgesTo = "allEdgesTo";

    /// <summary>Some node of <c>FromType</c> reaches some node of <c>ToType</c>.</summary>
    public const string PathExists = "pathExists";

    /// <summary>
    /// V4 VS4. The entry points still declare at least <c>Min</c> rps between them.
    ///
    /// <strong>Anti-cheese for every load challenge</strong>, and it had to exist the moment traffic
    /// became a property of the architecture (DT.1): without it, "get everything under 80 % of its
    /// capacity at 300 rps" is passed by typing 1 into the box.
    /// </summary>
    public const string DeclaresLoad = "declaresLoad";

    // ---- Analítica ---------------------------------------------------------

    /// <summary>The analyzer reports no finding with <c>Code</c>.</summary>
    public const string NoFinding = "noFinding";

    /// <summary>The analyzer reports at least one finding with <c>Code</c>. For challenges that teach how to provoke something.</summary>
    public const string HasFinding = "hasFinding";

    /// <summary>No finding of <c>Severity</c> <em>or worse</em>. Asking for "no warnings" while criticals slipped through would be a trap.</summary>
    public const string NoSeverity = "noSeverity";

    // ---- Simulada ----------------------------------------------------------

    /// <summary>Run the scenario in <c>Faults</c> and compare <c>Metric</c> against <c>Value</c>.</summary>
    public const string UnderScenario = "underScenario";

    public static bool IsKnown(string kind) => kind is
        ContainsNode or AbsentNode or MaxFanOut or AllEdgesTo or PathExists or DeclaresLoad or
        NoFinding or HasFinding or NoSeverity or
        UnderScenario;
}

/// <summary>
/// The mechanisms <see cref="RequirementKinds.AllEdgesTo"/> can ask for. The four that survive on an
/// edge after V3 D4.6 retired <c>bulkhead</c>, and no more: a requirement that names a mechanism the
/// model does not execute would be a checkbox, which is exactly what D3.5 of V3 removed.
/// </summary>
public static class RequirementMechanisms
{
    public const string Timeout = "timeout";
    public const string Retries = "retries";
    public const string CircuitBreaker = "circuitBreaker";
    public const string Fallback = "fallback";

    public static bool IsKnown(string mechanism) => mechanism is
        Timeout or Retries or CircuitBreaker or Fallback;
}

/// <summary>
/// The metrics a simulated requirement may read. <strong>There is deliberately no entry for the
/// Architecture Score</strong>, and that is D1.4 made mechanical rather than intentional: V4 adds
/// seven rules and two areas that were empty start scoring, so every existing score moves. A
/// challenge pinned to a score would un-pass solutions nobody touched.
///
/// <see cref="Simulation.SimulationMetrics"/> field names, one to one.
/// </summary>
public static class RequirementMetrics
{
    public const string ComponentsDown = "componentsDown";
    public const string ComponentsDegraded = "componentsDegraded";
    public const string BlastRadius = "blastRadius";
    public const string EntryPointsAffected = "entryPointsAffected";
    public const string NominalLatencyMs = "nominalLatencyMs";
    public const string FaultedLatencyMs = "faultedLatencyMs";
    public const string RequestAmplification = "requestAmplification";

    // V4 VS2's axis, now askable from a challenge.
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

    // Describe() used to turn an operator into "como mucho". It is `operator.lte` in the
    // catalogue now: the comparison is data, and the words for it belong wherever the sentence
    // that contains them is resolved (V7 D1.1).
}
