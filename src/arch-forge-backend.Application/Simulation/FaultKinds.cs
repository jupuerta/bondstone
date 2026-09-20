namespace bondstone_backend.Application.Simulation;

using bondstone_backend.Application.Analysis;

public static class FaultKinds
{
    public const string Down = "down";

    public const string Degraded = "degraded";

    public const string Latency = "latency";

    public static bool IsKnown(string kind) => kind is Down or Degraded or Latency;

    public static bool AppliesTo(string kind, ElementType target) =>
        kind is not Degraded || target == ElementType.Node;

    // A zone is not an element: it has no Guid, no timeout and no capacity, so the only thing that
    // can happen to it is that it stops existing. Everything else is a property of the components
    // inside it and is asked of them one by one.
    public static bool AppliesToZone(string kind) => kind == Down;

    public static bool RequiresValue(string kind) => kind == Latency;
}
