using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// V4 D5.6. The correction a finding can apply to the architecture, as data rather than prose.
///
/// <strong>Only five of the fifteen rules carry one</strong>, and the ten that do not are the point
/// of the decision. V3 called this debt <em>"siete reglas, siete propuestas: un proyecto, no una
/// funcionalidad"</em>. It stops being a project the moment you only automate the corrections where
/// <em>there is nothing to decide</em>: a missing timeout has one fix, a SPOF has four, and choosing
/// one of the four for the user is taking away the decision the product exists to teach (§21).
///
/// The transformation is described here and executed by the client over the graph it already owns,
/// which is why V4 gains no write endpoint for this: applying a fix is an edit like any other, and
/// the autosave persists it like any other.
/// </summary>
/// <param name="Element">What to change. Always one of the finding's own elements.</param>
/// <param name="Label">
/// The button's text. Says what will happen, with its numbers - which is why it is a
/// <see cref="LocalizedText"/> and not a key on its own: "Subir el timeout a 8 s" carries a value
/// the rule computed (V7 D1.1).
/// </param>
public sealed record AppliedFix(
    string Kind,
    FindingElement Element,
    LocalizedText Label,
    int? TimeoutSeconds = null,
    bool? CircuitBreaker = null,
    bool? Fallback = null,
    string? ConfigurationKey = null,
    bool? ConfigurationValue = null);

/// <summary>
/// The closed catalogue of transformations, in the same spirit as <c>FaultKinds</c> and
/// <c>RequirementKinds</c>: four shapes cover the five corrections, and anything the client does not
/// recognise it simply does not offer - the finding keeps its text, which is the fallback that makes
/// adding a fix later a non-breaking change.
/// </summary>
public static class FixKinds
{
    /// <summary>Merge timeout / circuit breaker / fallback into an edge's resilience.</summary>
    public const string SetEdgeResilience = "setEdgeResilience";

    /// <summary>Set one boolean in a node's configuration - today only <c>deadLetterQueue</c>.</summary>
    public const string SetNodeFlag = "setNodeFlag";

    /// <summary>
    /// Put a Cache in front of a Database: everything calling it directly is rewired through the
    /// cache, and the cache calls the database. The element is the database.
    /// </summary>
    public const string InsertCache = "insertCache";

    /// <summary>Remove a node. Only ever offered for one that is connected to nothing.</summary>
    public const string RemoveNode = "removeNode";

    public static bool IsKnown(string kind) => kind is
        SetEdgeResilience or SetNodeFlag or InsertCache or RemoveNode;
}
