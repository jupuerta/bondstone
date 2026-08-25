using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis;

public sealed record AppliedFix(
    string Kind,
    FindingElement Element,
    LocalizedText Label,
    int? TimeoutSeconds = null,
    bool? CircuitBreaker = null,
    bool? Fallback = null,
    string? ConfigurationKey = null,
    bool? ConfigurationValue = null,
    Guid? TargetNodeId = null);

public static class FixKinds
{
    public const string SetEdgeResilience = "setEdgeResilience";

    public const string SetNodeFlag = "setNodeFlag";

    public const string InsertCache = "insertCache";

    public const string RemoveNode = "removeNode";

    public const string ConnectTo = "connectTo";

    public const string MakeEdgeAsynchronous = "makeEdgeAsynchronous";

    public static bool IsKnown(string kind) => kind is
        SetEdgeResilience or SetNodeFlag or InsertCache or RemoveNode or ConnectTo or MakeEdgeAsynchronous;
}
