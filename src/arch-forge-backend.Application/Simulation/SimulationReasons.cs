using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Simulation;

public static class SimulationReasons
{
    public static LocalizedText ForNode(EffectReason reason, ReasonContext context) => reason switch
    {
        EffectReason.Injected => LocalizedText.Of("sim.node.injected"),

        EffectReason.Unreachable =>
            LocalizedText.Of("sim.node.unreachable", ("cause", context.Cause)),

        EffectReason.NoWork =>
            LocalizedText.Of("sim.node.noWork"),

        EffectReason.Unprotected =>
            LocalizedText.Of("sim.node.unprotected", ("target", context.Target)),

        EffectReason.NoTimeout =>
            LocalizedText.Of("sim.node.noTimeout", ("target", context.Target)),

        EffectReason.TimeoutExceeded =>
            LocalizedText.Of("sim.node.timeoutExceeded",
                ("target", context.Target),
                ("callMs", context.CallMs),
                ("timeoutMs", context.TimeoutMs)),

        EffectReason.Contained =>
            LocalizedText.Of("sim.node.contained", ("target", context.Target)),

        EffectReason.QueueDown =>
            LocalizedText.Of("sim.node.queueDown"),

        EffectReason.DegradedDependency =>
            LocalizedText.Of("sim.node.degradedDependency", ("target", context.Target)),

        EffectReason.InstanceDown => LocalizedText.Of(context.Contained
            ? "sim.node.instanceDown.contained"
            : "sim.node.instanceDown.spreading"),

        EffectReason.Saturated =>
            LocalizedText.Of("sim.node.saturated",
                ("utilization", context.Utilization),
                ("loadRps", context.LoadRps),
                ("capacityRps", context.CapacityRps)),

        EffectReason.Overloaded =>
            LocalizedText.Of("sim.node.overloaded",
                ("loadRps", context.LoadRps),
                ("capacityRps", context.CapacityRps),
                ("served", context.Utilization == 0m ? 100m : 100m / context.Utilization)),

        _ => LocalizedText.Of("sim.node.healthy")
    };

    public static LocalizedText ForConnection(EffectReason reason, ReasonContext context) => reason switch
    {
        EffectReason.Injected => LocalizedText.Of("sim.edge.injected"),

        EffectReason.Unreachable => LocalizedText.Of("sim.edge.unreachable", ("cause", context.Cause)),

        EffectReason.Unprotected => LocalizedText.Of("sim.edge.unprotected", ("target", context.Target)),

        EffectReason.NoTimeout =>
            LocalizedText.Of("sim.edge.noTimeout",
                ("target", context.Target),
                ("waitMs", LatencyModel.UnboundedWait)),

        EffectReason.TimeoutExceeded =>
            LocalizedText.Of("sim.edge.timeoutExceeded",
                ("callMs", context.CallMs),
                ("timeoutMs", context.TimeoutMs)),

        EffectReason.Contained => LocalizedText.Of("sim.edge.contained", ("target", context.Target)),

        EffectReason.QueueDown => LocalizedText.Of("sim.edge.queueDown"),

        EffectReason.DegradedDependency => LocalizedText.Of("sim.edge.degradedDependency", ("target", context.Target)),

        EffectReason.InstanceDown => LocalizedText.Of("sim.edge.instanceDown"),

        _ => LocalizedText.Of("sim.edge.healthy")
    };
}

public sealed record ReasonContext(
    string Target = "",
    string Cause = "",
    int CallMs = 0,
    int TimeoutMs = 0,
    bool Contained = false,
    decimal LoadRps = 0m,
    decimal CapacityRps = 0m,
    decimal Utilization = 0m);
