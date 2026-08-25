namespace arch_forge_backend.Application.Simulation;

using arch_forge_backend.Application.Analysis;

public static class FaultKinds
{
    public const string Down = "down";

    public const string Degraded = "degraded";

    public const string Latency = "latency";

    public static bool IsKnown(string kind) => kind is Down or Degraded or Latency;

    public static bool AppliesTo(string kind, ElementType target) =>
        kind is not Degraded || target == ElementType.Node;

    public static bool RequiresValue(string kind) => kind == Latency;
}
