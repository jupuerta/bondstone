using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// V3 plan técnico §7. These are product, not filler: it is what the user reads when they click a
/// red component, and the difference between a simulator that paints colours and one that explains
/// an outage. The wording says what happened and, where it matters, why the mechanism that was
/// supposed to help did not get to run.
///
/// They live apart from <see cref="EdgeEffect"/> so that the semantics stays testable without
/// names: the pure function returns an <see cref="EffectReason"/>, and this turns it into a
/// sentence about actual components.
///
/// <strong>V7 D1.4. This file barely changed, and that is the point.</strong> The simulator has
/// carried the shape localization needs since V3 - an <see cref="EffectReason"/> saying <em>which</em>
/// sentence and a <see cref="ReasonContext"/> carrying <em>what goes in it</em> - and was rendering
/// it to Spanish one step too early. So the switch now returns the key and the arguments instead of
/// the prose, and the arguments it already had needed no rethinking. The analysis rules had to be
/// taken apart for this; the simulator only had to stop at the step before.
/// </summary>
public static class SimulationReasons
{
    public static LocalizedText ForNode(EffectReason reason, ReasonContext context) => reason switch
    {
        EffectReason.Injected => LocalizedText.Of("sim.node.injected"),

        EffectReason.Unreachable =>
            LocalizedText.Of("sim.node.unreachable", ("cause", context.Cause)),

        // D4.7: a consumer with no work is alive and idle, not dead. Telling the two apart is part
        // of what the simulator teaches.
        EffectReason.NoWork =>
            LocalizedText.Of("sim.node.noWork"),

        EffectReason.Unprotected =>
            LocalizedText.Of("sim.node.unprotected", ("target", context.Target)),

        // D4.2: the box that was ticked does nothing here, and saying so is the whole lesson.
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

        // V4 DT.5. The number is the message: "it rejects 43 %" is something you can act on,
        // "it is saturated" is not.
        EffectReason.Saturated =>
            LocalizedText.Of("sim.node.saturated",
                ("utilization", context.Utilization),
                ("loadRps", context.LoadRps),
                ("capacityRps", context.CapacityRps)),

        EffectReason.Overloaded =>
            LocalizedText.Of("sim.node.overloaded",
                ("loadRps", context.LoadRps),
                ("capacityRps", context.CapacityRps),
                // Served out of a hundred, worked out here rather than in the template: a
                // catalogue that can divide is a catalogue that can be wrong in one language only.
                ("served", context.Utilization == 0m ? 100m : 100m / context.Utilization)),

        _ => LocalizedText.Of("sim.node.healthy")
    };

    /// <summary>
    /// The same events told from the call's side rather than the caller's. An edge that says
    /// "su llamada" would be talking about itself in the third person.
    /// </summary>
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

/// <param name="Target">The name of the dependency the call points at.</param>
/// <param name="Cause">The name of the element that cut the path, for phase B.</param>
/// <param name="CallMs">What the call costs when it is the cost that blew the timeout.</param>
public sealed record ReasonContext(
    string Target = "",
    string Cause = "",
    int CallMs = 0,
    int TimeoutMs = 0,
    bool Contained = false,
    decimal LoadRps = 0m,
    decimal CapacityRps = 0m,
    decimal Utilization = 0m);
